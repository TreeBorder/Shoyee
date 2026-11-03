namespace Shoyee.Contracts.Options;

/// <summary>
/// 会话上下文管理。目前只有工作模式的入循环压缩；RP 走 <c>RoleplaySummarizer</c> 的
/// 每 30 轮全局摘要，不读这一段。
/// </summary>
/// <param name="Compaction">压缩参数；null = 用 <see cref="CompactionOptions.Default"/>。</param>
public sealed record ContextOptions(CompactionOptions? Compaction = null)
{
    /// <summary>实际生效的压缩参数。</summary>
    public CompactionOptions EffectiveCompaction => Compaction ?? CompactionOptions.Default;
}

/// <summary>
/// 工作模式的上下文压缩（框架 Harness 的入循环 CompactionProvider）。
///
/// 默认**关闭**：压缩会改写下一次请求的前缀，既可能丢内容也会让 prompt cache 失效，
/// 不把它当成常规手段。打开后只在高水位兜底：
/// **500K 以下绝不压缩**；触发一次后压到目标水位，并至少隔 30 分钟才会再次触发。
/// 它只影响下一次请求实际发出去的消息，不改写已落库的会话历史。
/// </summary>
/// <param name="Enabled">是否启用（默认关）。</param>
/// <param name="MaxContextWindowTokens">模型上下文窗口（token），用来给触发水位设上限。</param>
/// <param name="MaxOutputTokens">单次回复的输出上限；窗口减去它才是可用的输入预算。按模型规格填。</param>
/// <param name="TriggerTokens">触发水位：达到这个 token 数才压缩；低于 500K 的配置会被抬到 500K 以上。</param>
/// <param name="TargetTokens">目标水位：压缩到低于这个值就停，留出回涨空间；不得低于 500K，也不得高于触发水位。</param>
/// <param name="MinimumPreservedGroups">最近至少保留多少个非系统消息组（工具调用与结果算一组）。</param>
/// <param name="MinimumIntervalMinutes">两次压缩之间的最短间隔（分钟）；防止一次没压够就在下一轮继续压。</param>
public sealed record CompactionOptions(
    bool Enabled = false,
    int MaxContextWindowTokens = 1_000_000,
    int MaxOutputTokens = 384_000,
    int TriggerTokens = 700_000,
    int TargetTokens = 500_000,
    int MinimumPreservedGroups = 32,
    int MinimumIntervalMinutes = 30)
{
    /// <summary>默认参数：关闭；即使打开也是 700K 触发、压到 500K、至少保留 32 组、间隔 30 分钟。</summary>
    public static CompactionOptions Default { get; } = new();

    /// <summary>压缩的硬下限：上下文不到 500K 一律不压。</summary>
    public const int FloorTokens = 500_000;
}
