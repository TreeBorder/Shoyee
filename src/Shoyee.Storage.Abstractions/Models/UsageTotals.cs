namespace Shoyee.Storage.Abstractions;

/// <summary>用量汇总。</summary>
/// <param name="Calls">调用次数。</param>
/// <param name="InputTokens">输入 token。</param>
/// <param name="OutputTokens">输出 token。</param>
/// <param name="CacheHitTokens">缓存命中。</param>
/// <param name="CacheMissTokens">缓存未命中。</param>
public sealed record UsageTotals(
    int Calls,
    int InputTokens,
    int OutputTokens,
    int CacheHitTokens,
    int CacheMissTokens);
