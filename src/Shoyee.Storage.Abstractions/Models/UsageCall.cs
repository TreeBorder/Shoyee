namespace Shoyee.Storage.Abstractions;

/// <summary>一次模型调用的用量（<c>usage</c> 表里的一行）。</summary>
/// <param name="InputTokens">输入 token。</param>
/// <param name="OutputTokens">输出 token。</param>
/// <param name="CacheHitTokens">输入里命中缓存的部分。</param>
/// <param name="CreatedAt">调用时间（UTC）。</param>
public sealed record UsageCall(
    int InputTokens,
    int OutputTokens,
    int CacheHitTokens,
    DateTimeOffset CreatedAt);
