namespace Shoyee.Sessions.Summary;

/// <summary>一次 RP 摘要请求：已拼好的 prompt 与覆盖范围。</summary>
/// <param name="Prompt">发给摘要模型的用户消息全文。</param>
/// <param name="ThroughRound">本次摘要覆盖到的全局轮次号。</param>
/// <param name="RoundCount">本次新增覆盖的轮数。</param>
public sealed record RoleplaySummaryRequest(
    string Prompt,
    int ThroughRound,
    int RoundCount);
