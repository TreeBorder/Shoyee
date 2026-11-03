import AppKit
import Foundation

// 常驻 bundle 筱义.app 的入口。
//
// 两种启动方式：
//   shoyee --notify <标题> <正文> [--silent]   daemon 发系统通知（UNUserNotificationCenter 要求进程有 bundle id）
//   shoyee                                     菜单栏（LSUIElement：不占 Dock，只在状态栏）
//
// 菜单栏不直接连 socket：所有调用都走同一目录下的 shoyee-ctl（见 docs/架构与数据模型.md §7.1）。

let arguments = CommandLine.arguments

if arguments.count >= 4, arguments[1] == "--notify" {
    Notifier.run(title: arguments[2], body: arguments[3], silent: arguments.dropFirst(4).contains("--silent"))
} else {
    MenuBarApp.run()
}
