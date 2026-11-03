namespace Shoyee.Desktop.Rpc;

/// <summary>桌面与 daemon 的连接状态。</summary>
public enum DaemonStatus
{
    /// <summary>没有连接（daemon 没起 / 连接断了，后台在重试）。</summary>
    Disconnected,

    /// <summary>正在建立连接（首次连接或断线重连中）。</summary>
    Connecting,

    /// <summary>已握手，可以收发 RPC。</summary>
    Connected,
}

/// <summary>一次连接状态变化。</summary>
/// <param name="Status">当前状态。</param>
/// <param name="Workspace">hello 返回的工作区（只有 <see cref="DaemonStatus.Connected"/> 时有值）。</param>
/// <param name="Error">最近一次失败原因（连接正常时为 null）。</param>
public sealed record DaemonConnectionState(
    DaemonStatus Status,
    string? Workspace = null,
    string? Error = null);
