using Shoyee.Contracts.Domain;

namespace Shoyee.Gateway.Rpc;

/// <summary>一条连接的握手状态。hello 之前 <see cref="Role"/> 为 null、<see cref="LaneId"/> 为 null。</summary>
public sealed class RpcConnectionState
{
    /// <summary>客户端自报类型（desktop / menu / ctl / test）。</summary>
    public string Client { get; internal set; } = "unknown";

    /// <summary>客户端自报版本。</summary>
    public string? ClientVersion { get; internal set; }

    /// <summary>连接角色；hello 之前为 null（任何方法都不放行）。</summary>
    public ClientRole? Role { get; internal set; }

    /// <summary>桌面 RP 连接订阅的 Lane id。</summary>
    public string? LaneId { get; internal set; }

    /// <summary>是否已完成 hello。</summary>
    public bool HelloCompleted { get; internal set; }

    /// <summary>已建立连接的显示名（日志用）。</summary>
    public string DisplayName => $"{Client}/{(Role?.ToString() ?? "unverified")}"
        + (LaneId is null ? string.Empty : $"@{LaneId}");
}
