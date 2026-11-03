namespace Shoyee.Notify.System;

/// <summary>
/// macOS 系统通知配置。通知由常驻 bundle <c>筱.app</c> 发送——Apple 要求进程有 bundle id，
/// 所以 daemon 通过 <c>筱.app/Contents/MacOS/shoyee --notify</c> 调它，权限只授权这一个 App。
/// </summary>
/// <param name="ExecutablePath">
/// 常驻 App 的可执行文件路径；空 = 同目录的 shoyee（daemon 已在 bundle 内）或
/// <c>{daemon 输出目录}/筱.app/Contents/MacOS/shoyee</c>（开发布局）。
/// </param>
/// <param name="Sound">是否带默认提示音（默认 true）。</param>
/// <param name="TimeoutSeconds">等待通知发出的超时。</param>
/// <param name="FallbackToScript">
/// 常驻 App 因为签名被系统拒绝时，退回 <c>osascript display notification</c>（默认关）。
/// 兜底能弹通知，但归属是「脚本编辑器」；拿到 Apple 签名证书后应关掉。
/// </param>
public sealed record SystemNotifyOptions(
    string ExecutablePath = "",
    bool Sound = true,
    int TimeoutSeconds = 15,
    bool FallbackToScript = false);
