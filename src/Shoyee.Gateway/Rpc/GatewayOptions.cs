using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Gateway.Rpc;

/// <summary>Gateway 的进程级参数（由 Daemon 组合根填）。</summary>
public sealed record GatewayOptions
{
    /// <summary>协议/服务端版本（<see cref="Contracts.Rpc.RpcProtocol.Version"/>）。</summary>
    public required string Version { get; init; }

    /// <summary>工作区根目录（握手与 status.get 回报；不参与 socket 路径）。</summary>
    public required string Workspace { get; init; }

    /// <summary>UDS 路径。</summary>
    public required string SocketPath { get; init; }

    /// <summary>应用数据根目录（日志 / 头像）；null = 用 <c>SHOYEE_HOME</c> 或系统默认位置。</summary>
    public string? AppDataRoot { get; init; }

    /// <summary>daemon 启动时间。</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>库文件信息（status.get 用；null = 暂不回报体积）。</summary>
    public Func<DbInfo>? DatabaseInfo { get; init; }

    /// <summary>是否允许 <see cref="Contracts.Domain.ClientRole.Test"/> 连接（仅测试环境）。</summary>
    public bool AllowTestRole { get; init; }

    /// <summary>优雅退出时等待在跑/排队轮次的上限（默认 10 秒）。</summary>
    public TimeSpan ShutdownTurnGrace { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>取消轮次后再等它们收尾的上限。</summary>
    public TimeSpan ShutdownCancelGrace { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary><c>daemon.status</c> 心跳推送间隔（菜单栏靠它刷新）。</summary>
    public TimeSpan StatusPushInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>握手回报的能力列表（只列本实例真能用的；默认基础能力，Daemon 按配置追加）。</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = RpcProtocol.Capabilities;
}
