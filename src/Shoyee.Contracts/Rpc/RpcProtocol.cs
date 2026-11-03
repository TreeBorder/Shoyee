namespace Shoyee.Contracts.Rpc;

/// <summary>
/// 传输层与握手的固定参数（服务端与客户端共用，避免两边各写一份数字）。
/// 语义见 <c>docs/IPC契约.md</c> §1、§2。
/// </summary>
public static class RpcProtocol
{
    /// <summary>协议版本。主版本不同即拒连（<c>-32010</c>）。</summary>
    public const string Version = "1.0.0";

    /// <summary>单帧上限（8 MiB），超限断开并回 <c>-32003</c>。</summary>
    public const int MaxFrameBytes = 8 * 1024 * 1024;

    /// <summary>连接后必须在此时间内完成 hello。</summary>
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);

    /// <summary>客户端建议的 ping 间隔。</summary>
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(20);

    /// <summary>服务端空闲回收阈值：超过这么久没收到任何帧即断开。</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 本版本基础能力；Daemon 按配置追加 <c>photo</c> / <c>image</c> / <c>tts</c>。
    /// 只列**真能用**的。
    /// </summary>
    public static IReadOnlyList<string> Capabilities { get; } = ["turn.stream", "approval", "cron", "asset", "tools", "mcp", "state", "usage", "config.set", "config.reload"];

    /// <summary>取版本字符串的主版本号；解析不出返回 null。</summary>
    /// <param name="version">版本字符串。</param>
    public static int? MajorOf(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var head = version.Trim().Split('.')[0];
        return int.TryParse(head, out var major) ? major : null;
    }
}
