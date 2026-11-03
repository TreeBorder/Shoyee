namespace Shoyee.Storage.Abstractions;

/// <summary>一次真实发送的实测记录（微信窗口期靠它收敛）。</summary>
/// <param name="LaneId">Lane id。</param>
/// <param name="SinceLastInboundSeconds">距上次入站的秒数。</param>
/// <param name="Result">结果（ok / failed / window_closed …）。</param>
/// <param name="Error">错误详情。</param>
public sealed record SendAttemptRecord(
    string LaneId,
    long SinceLastInboundSeconds,
    string Result,
    string? Error);
