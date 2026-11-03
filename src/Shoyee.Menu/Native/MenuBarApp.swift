import AppKit
import Foundation

// 菜单栏：状态一览 + 启停/重载/通知/日志。
// 聊天不在菜单栏里——那是桌面（Avalonia）的事，这里只做「常驻状态入口」。

final class MenuBarApp: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private var status: DaemonStatus?
    private var statusError: String?
    private var lastAction: String?
    private var refreshing = false
    private var timer: Timer?

    static func run() -> Never {
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        let delegate = MenuBarApp()
        app.delegate = delegate
        app.run()
        exit(0)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            button.image = Self.statusImage()
            button.toolTip = "筱"
        }

        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
        rebuild()

        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: 20, repeats: true) { [weak self] _ in
            self?.refresh()
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate()
    }

    /// 菜单栏图标：Resources/menubar.png（由 meinv.svg 渲染的剪影）。
    /// 用模板色，跟着菜单栏明暗自动反色；资源缺失时退回 SF Symbol。
    private static func statusImage() -> NSImage? {
        guard let url = Bundle.main.url(forResource: "menubar", withExtension: "png"),
              let image = NSImage(contentsOf: url) else {
            let fallback = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "筱")
            fallback?.isTemplate = true
            return fallback
        }

        image.isTemplate = true
        image.size = NSSize(width: 17, height: 17)
        return image
    }

    // MARK: - 刷新

    /// 菜单将要打开时先拿一次最新状态（下拉里先显示上一次的结果，刷新回来再重建）。
    func menuWillOpen(_ menu: NSMenu) {
        refresh()
    }

    private func refresh() {
        if refreshing {
            return
        }
        refreshing = true

        DispatchQueue.global(qos: .utility).async {
            let result = CtlClient.run(["status", "--json"])
            DispatchQueue.main.async {
                self.refreshing = false
                self.apply(result)
                self.rebuild()
            }
        }
    }

    private func apply(_ result: CtlOutput) {
        guard result.code == 0,
              let data = result.stdout.data(using: .utf8),
              let decoded = try? JSONDecoder().decode(DaemonStatus.self, from: data) else {
            status = nil
            // 退出码 2 = 没在跑，这是正常状态，不是错误
            statusError = result.code == 2 ? nil : result.message
            return
        }

        status = decoded
        statusError = nil
    }

    // MARK: - 菜单

    private func rebuild() {
        guard let menu = statusItem.menu else { return }
        menu.removeAllItems()

        if status != nil {
            addText("筱 运行中 · v\(status!.version) · \(Self.duration(status!.uptimeSeconds))")
        } else {
            addText("筱 未运行")
        }

        if let error = statusError {
            addText("状态获取失败：\(error)")
        }

        if let action = lastAction {
            addText(action)
        }

        menu.addItem(.separator())
        addAction("打开界面", #selector(openDesktop))

        if let status = status, !status.lanes.isEmpty {
            menu.addItem(.separator())
            for lane in status.lanes {
                let mark = lane.connected ? "●" : "○"
                var title = "\(mark) \(Self.laneLabel(lane))"
                if let error = lane.lastError, !error.isEmpty {
                    title += " — \(error)"
                }
                addText(title)
            }
        }

        menu.addItem(.separator())
        addAction("重载配置", #selector(reloadConfig))
        addAction("发送测试通知", #selector(testNotify))
        addAction("打开日志", #selector(openLog))

        menu.addItem(.separator())
        if status == nil {
            addAction("启动 daemon", #selector(startDaemon))
        } else {
            addAction("重启 daemon", #selector(restartDaemon))
            addAction("停止 daemon", #selector(stopDaemon))
        }

        menu.addItem(.separator())
        addAction("退出菜单栏（daemon 继续运行）", #selector(quit))

        statusItem.button?.appearsDisabled = status == nil
        statusItem.button?.toolTip = status == nil ? "筱未运行" : "筱运行中"
    }

    private func addText(_ text: String) {
        let item = NSMenuItem(title: text, action: nil, keyEquivalent: "")
        item.isEnabled = false
        statusItem.menu?.addItem(item)
    }

    private func addAction(_ title: String, _ selector: Selector) {
        let item = NSMenuItem(title: title, action: selector, keyEquivalent: "")
        item.target = self
        statusItem.menu?.addItem(item)
    }

    // MARK: - 动作

    /// 桌面端是同一个 bundle 的主可执行文件（Contents/MacOS/shoyee-desktop），按需启动。
    /// 已在运行就激活窗口，不重复起进程。
    @objc private func openDesktop() {
        let url = Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/shoyee-desktop")
        guard FileManager.default.isExecutableFile(atPath: url.path) else {
            lastAction = "找不到桌面端：\(url.path)"
            rebuild()
            return
        }

        if let running = NSWorkspace.shared.runningApplications.first(where: {
            $0.executableURL?.standardizedFileURL == url.standardizedFileURL
        }) {
            running.activate(options: [.activateAllWindows])
            return
        }

        let process = Process()
        process.executableURL = url
        do {
            try process.run()
            lastAction = "正在打开界面…"
        } catch {
            lastAction = "打开界面失败：\(error.localizedDescription)"
        }
        rebuild()
    }

    @objc private func startDaemon() {
        perform(["start"], success: "daemon 已启动")
    }

    @objc private func stopDaemon() {
        perform(["stop"], success: "daemon 已停止")
    }

    @objc private func restartDaemon() {
        perform(["restart"], success: "daemon 已重启")
    }

    @objc private func reloadConfig() {
        perform(["reload"], success: "配置已重载")
    }

    @objc private func testNotify() {
        perform(["notify"], success: "测试通知已发送")
    }

    @objc private func openLog() {
        let manager = FileManager.default
        if manager.fileExists(atPath: AppPaths.logFile.path) {
            NSWorkspace.shared.open(AppPaths.logFile)
            return
        }

        statusItem.menu?.cancelTracking()
        if manager.fileExists(atPath: AppPaths.logDirectory.path) {
            NSWorkspace.shared.open(AppPaths.logDirectory)
        } else {
            lastAction = "还没有日志：\(AppPaths.logFile.path)"
            rebuild()
        }
    }

    @objc private func quit() {
        NSApplication.shared.terminate(nil)
    }

    private func perform(_ arguments: [String], success: String) {
        lastAction = "正在执行…"
        rebuild()

        DispatchQueue.global(qos: .userInitiated).async {
            let result = CtlClient.run(arguments)
            DispatchQueue.main.async {
                if result.ok {
                    self.lastAction = "\(success)（\(Self.now())）"
                } else {
                    self.lastAction = "失败：\(result.message)"
                }
                self.refresh()
            }
        }
    }

    // MARK: - 格式

    /// 渠道 + 模式，不拼角色名（与桌面端 lane 展示保持一致）。
    private static func laneLabel(_ lane: LaneStatus) -> String {
        // 服务端用 camelCase 枚举（WeChat → weChat），这里统一按小写比较，避免落到 displayName。
        switch lane.channel.lowercased() {
        case "desktop":
            return "桌面"
        case "mattermost":
            return lane.mode == "roleplay" ? "Mattermost(角色)" : "Mattermost(工作)"
        case "wechat":
            return "微信(工作)"
        default:
            return lane.displayName ?? lane.id
        }
    }

    private static func duration(_ seconds: Int) -> String {
        if seconds >= 86_400 {
            return "\(seconds / 86_400) 天 \(seconds % 86_400 / 3_600) 小时"
        }
        if seconds >= 3_600 {
            return "\(seconds / 3_600) 小时 \(seconds % 3_600 / 60) 分"
        }
        if seconds >= 60 {
            return "\(seconds / 60) 分 \(seconds % 60) 秒"
        }
        return "\(seconds) 秒"
    }

    private static func now() -> String {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss"
        return formatter.string(from: Date())
    }
}
