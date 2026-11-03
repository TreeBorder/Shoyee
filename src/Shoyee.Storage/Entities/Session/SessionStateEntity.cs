namespace Shoyee.Storage.Entities.Session;

/// <summary>框架序列化的会话状态（含历史、开场 system、压缩点、绑定）。不做改写。</summary>
public sealed class SessionStateEntity
{
    /// <summary>存储键（session id，可带分区）。</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary><c>AIAgent.SerializeSessionAsync</c> 的产物。</summary>
    public string StateJson { get; set; } = string.Empty;

    /// <summary>更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
