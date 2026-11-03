import AppKit
import Foundation
import UserNotifications

// 系统通知入口（daemon 的 notify.provider = system 时调用 `筱义.app/Contents/MacOS/shoyee --notify ...`）。
// 必须让 NSApplication 跑起来：授权框与投递回调都要求正常的 GUI 宿主。

func writeError(_ text: String) {
    FileHandle.standardError.write((text + "\n").data(using: .utf8)!)
}

enum Notifier {
    /// 发一条通知并退出进程（不返回）。
    static func run(title: String, body: String, silent: Bool) -> Never {
        let completion = Completion()
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)

        let delegate = Delegate(title: title, body: body, silent: silent, completion: completion)
        app.delegate = delegate

        // 授权框是异步的；超时就报错而不是永远挂住。
        DispatchQueue.main.asyncAfter(deadline: .now() + 10) {
            if !completion.isFinished() {
                writeError("通知发送超时。")
                exit(1)
            }
        }

        // 轮询完成状态并退出。
        DispatchQueue.global().async {
            while !completion.isFinished() {
                Thread.sleep(forTimeInterval: 0.05)
            }
            if let error = completion.error() {
                writeError(error)
                exit(1)
            }
            exit(0)
        }

        app.run()
        exit(0)
    }

    final class Completion {
        private let lock = NSLock()
        private var finished = false
        private var failure: String?

        func finish(_ error: String?) {
            lock.lock()
            if !finished {
                finished = true
                failure = error
            }
            lock.unlock()
        }

        func isFinished() -> Bool {
            lock.lock()
            defer { lock.unlock() }
            return finished
        }

        func error() -> String? {
            lock.lock()
            defer { lock.unlock() }
            return failure
        }
    }

    final class Delegate: NSObject, NSApplicationDelegate {
        private let title: String
        private let body: String
        private let silent: Bool
        private let completion: Completion

        init(title: String, body: String, silent: Bool, completion: Completion) {
            self.title = title
            self.body = body
            self.silent = silent
            self.completion = completion
        }

        func applicationDidFinishLaunching(_ notification: Notification) {
            let center = UNUserNotificationCenter.current()
            center.requestAuthorization(options: [.alert, .sound]) { granted, error in
                if let error = error {
                    let nsError = error as NSError
                    if nsError.domain == "UNErrorDomain" && nsError.code == 1 {
                        self.completion.finish(
                            "通知授权失败：\(error.localizedDescription)。" +
                            "先在「系统设置 → 通知 → 筱」里允许通知；" +
                            "或把 notify.provider 切到 serverchan/email。")
                    } else {
                        self.completion.finish("通知授权失败：\(error.localizedDescription)")
                    }
                    return
                }

                guard granted else {
                    self.completion.finish("通知权限被拒绝。请在「系统设置 → 通知 → 筱」里允许通知。")
                    return
                }

                let content = UNMutableNotificationContent()
                content.title = self.title
                content.body = self.body
                if !self.silent {
                    content.sound = .default
                }

                let request = UNNotificationRequest(
                    identifier: UUID().uuidString,
                    content: content,
                    trigger: nil)
                center.add(request) { error in
                    self.completion.finish(error.map { "通知发送失败：\($0.localizedDescription)" })
                }
            }
        }
    }
}
