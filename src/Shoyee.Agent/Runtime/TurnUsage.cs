using Microsoft.Extensions.AI;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Agent.Runtime;

/// <summary>一次模型调用的用量（一轮里工具循环会调用多次，各记一条）。</summary>
/// <param name="InputTokens">输入 token。</param>
/// <param name="OutputTokens">输出 token。</param>
/// <param name="CacheHitTokens">输入里命中缓存的部分。</param>
public sealed record UsageCall(int InputTokens, int OutputTokens, int CacheHitTokens);

/// <summary>
/// 一轮的用量累加器：一次回合里每发生一次模型调用（工具循环会多次）就累加一次，
/// 最终汇成 <see cref="UsageSummary"/>（<c>turn.completed</c> 与 <c>usage</c> 表共用）。
/// </summary>
public sealed class TurnUsage
{
    private readonly List<UsageCall> _calls = [];

    /// <summary>本轮每次模型调用的明细（按发生顺序）。</summary>
    public IReadOnlyList<UsageCall> CallDetails => _calls;

    /// <summary>模型调用次数。</summary>
    public int Calls => _calls.Count;

    /// <summary>最后一次调用的上下文（已用 = 输入 + 输出）；没有数据时 null。</summary>
    public UsageCall? LastCall => _calls.Count == 0 ? null : _calls[^1];

    /// <summary>输入 token 合计。</summary>
    public int InputTokens { get; private set; }

    /// <summary>输出 token 合计。</summary>
    public int OutputTokens { get; private set; }

    /// <summary>缓存命中输入 token 合计。</summary>
    public int CacheHitTokens { get; private set; }

    /// <summary>是否有任何用量数据（端点不回 usage 时保持 false）。</summary>
    public bool HasData => _calls.Count > 0;

    /// <summary>累加一次模型调用的用量。</summary>
    /// <param name="details">流式 update / 响应里的用量。</param>
    public void Add(UsageDetails? details)
    {
        if (details is null)
        {
            return;
        }

        var input = details.InputTokenCount ?? 0;
        var output = details.OutputTokenCount ?? 0;
        var cached = details.CachedInputTokenCount ?? 0;
        if (input == 0 && output == 0 && cached == 0 && details.TotalTokenCount is null or 0)
        {
            return;
        }

        _calls.Add(new UsageCall((int)input, (int)output, (int)cached));
        InputTokens += (int)input;
        OutputTokens += (int)output;
        CacheHitTokens += (int)cached;
    }

    /// <summary>转成契约用量汇总；未命中 = 输入 - 命中（不出现负数）。</summary>
    public UsageSummary ToSummary() => new(
        _calls.Count,
        InputTokens,
        OutputTokens,
        CacheHitTokens,
        Math.Max(0, InputTokens - CacheHitTokens));
}
