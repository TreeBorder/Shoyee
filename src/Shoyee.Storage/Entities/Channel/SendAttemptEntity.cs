namespace Shoyee.Storage.Entities.Channel;

/// <summary>每次真实发送的实测记录（微信窗口期靠它收敛）。</summary>
public sealed class SendAttemptEntity
{
    /// <summary>自增 id。</summary>
    public long Id { get; set; }

    /// <summary>Lane id。</summary>
    public string LaneId { get; set; } = string.Empty;

    /// <summary>距上次入站的秒数。</summary>
    public long SinceLastInboundSeconds { get; set; }

    /// <summary>结果（ok / failed / window_closed …）。</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>错误详情。</summary>
    public string? Error { get; set; }

    /// <summary>时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
