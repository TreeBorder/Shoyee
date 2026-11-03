import Foundation

// 菜单栏唯一的外部调用：执行同目录的 shoyee-ctl，读它的 stdout/stderr 与退出码。
// 不在 Swift 里重写一遍 UDS 帧编解码——共用 C# 的 Shoyee.Client 才不会两边分叉。

struct CtlOutput {
    let code: Int32
    let stdout: String
    let stderr: String

    var ok: Bool { code == 0 }

    var message: String {
        let text = (stderr.isEmpty ? stdout : stderr).trimmingCharacters(in: .whitespacesAndNewlines)
        return text.isEmpty ? "退出码 \(code)" : text
    }
}

enum CtlClient {
    /// 找 shoyee-ctl：SHOYEE_CTL → 可执行文件同目录 → bundle 的 Resources → PATH。
    static func locate() -> URL? {
        if let custom = ProcessInfo.processInfo.environment["SHOYEE_CTL"], !custom.isEmpty {
            return URL(fileURLWithPath: custom)
        }

        var candidates: [URL] = []
        if let executable = Bundle.main.executableURL {
            let directory = executable.deletingLastPathComponent()
            candidates.append(directory.appendingPathComponent("shoyee-ctl"))
            candidates.append(
                directory.deletingLastPathComponent()
                    .appendingPathComponent("Resources/shoyee-ctl"))
        }

        candidates.append(URL(fileURLWithPath: "/usr/local/bin/shoyee-ctl"))
        candidates.append(URL(fileURLWithPath: "/opt/homebrew/bin/shoyee-ctl"))

        return candidates.first { FileManager.default.isExecutableFile(atPath: $0.path) }
    }

    /// 同步执行一条 ctl 命令（调用方负责放到后台队列）。
    static func run(_ arguments: [String], timeout: TimeInterval = 60) -> CtlOutput {
        guard let ctl = locate() else {
            return CtlOutput(
                code: -1,
                stdout: "",
                stderr: "找不到 shoyee-ctl；设置 SHOYEE_CTL，或用 install-app.sh 安装完整的 筱义.app。")
        }

        let process = Process()
        process.executableURL = ctl
        process.arguments = arguments
        let out = Pipe()
        let err = Pipe()
        process.standardOutput = out
        process.standardError = err

        let finished = DispatchSemaphore(value: 0)
        process.terminationHandler = { _ in finished.signal() }

        do {
            try process.run()
        } catch {
            return CtlOutput(code: -1, stdout: "", stderr: "无法执行 shoyee-ctl：\(error.localizedDescription)")
        }

        if finished.wait(timeout: .now() + timeout) == .timedOut {
            process.terminate()
            _ = finished.wait(timeout: .now() + 2)
            return CtlOutput(code: -1, stdout: "", stderr: "shoyee-ctl 超时（\(Int(timeout))s）。")
        }

        // 进程已退出，管道里剩下的就是全部输出（ctl 的输出都很短，顺序读完即可）。
        let outData = out.fileHandleForReading.readDataToEndOfFile()
        let errData = err.fileHandleForReading.readDataToEndOfFile()

        return CtlOutput(
            code: process.terminationStatus,
            stdout: String(data: outData, encoding: .utf8) ?? "",
            stderr: String(data: errData, encoding: .utf8) ?? "")
    }
}
