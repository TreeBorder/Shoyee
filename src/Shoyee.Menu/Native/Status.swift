import Foundation

// daemon.status 的线上形状（UTF-8 JSON，camelCase）。
// 只声明菜单栏用得到的字段；多出来的字段由 Codable 忽略。

struct LaneStatus: Codable {
    let id: String
    let channel: String
    let mode: String
    let displayName: String?
    let connected: Bool
    let activeSessionId: String?
    let lastError: String?
}

struct DaemonStatus: Codable {
    let version: String
    let uptimeSeconds: Int
    let socketPath: String
    let lanes: [LaneStatus]
}

// 路径规则与 C# 的 ShoyeePaths 保持一致：默认 ~/Library/Application Support/Shoyee，SHOYEE_HOME 可覆盖。
enum AppPaths {
    static var home: URL {
        if let custom = ProcessInfo.processInfo.environment["SHOYEE_HOME"], !custom.isEmpty {
            return URL(fileURLWithPath: custom)
        }
        return URL(fileURLWithPath: NSHomeDirectory())
            .appendingPathComponent("Library/Application Support/Shoyee")
    }

    static var logFile: URL { home.appendingPathComponent("logs/daemon.log") }

    static var logDirectory: URL { home.appendingPathComponent("logs") }

    static var socketFile: URL { home.appendingPathComponent("run/shoyee.sock") }
}
