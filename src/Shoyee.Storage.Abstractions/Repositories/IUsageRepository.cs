namespace Shoyee.Storage.Abstractions;

/// <summary>用量仓库。</summary>
public interface IUsageRepository
{
    /// <summary>记一次模型调用。</summary>
    Task AddAsync(
        string? sessionId,
        string? turnId,
        string purpose,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheHitTokens,
        int cacheMissTokens,
        CancellationToken cancellationToken = default);

    /// <summary>时间区间内的用量汇总（含起止）；<paramref name="sessionId"/> 非 null 时只看该会话。</summary>
    Task<UsageTotals> GetTotalsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 该会话最后一次主对话调用（<c>purpose = chat.call</c>）；没有记录时 null。
    /// 自动起名等旁路调用不算，避免把标题小请求当成上下文占用；
    /// 旧版本按轮聚合写入的 <c>chat</c> 行也不取，宁可回退到桌面端估算。
    /// </summary>
    Task<UsageCall?> GetLastCallAsync(string sessionId, CancellationToken cancellationToken = default);
}
