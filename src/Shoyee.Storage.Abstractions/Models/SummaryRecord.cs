namespace Shoyee.Storage.Abstractions;

/// <summary>一条 RP 摘要（全局累积全文，不按 Lane 分）。</summary>
/// <param name="Id">自增 id。</param>
/// <param name="ThroughRound">已覆盖到的全局轮次号。</param>
/// <param name="RoundCount">本次新增覆盖的轮数。</param>
/// <param name="Content">累积摘要全文。</param>
/// <param name="CreatedAt">生成时间。</param>
public sealed record SummaryRecord(
    long Id,
    int ThroughRound,
    int RoundCount,
    string Content,
    DateTimeOffset CreatedAt);
