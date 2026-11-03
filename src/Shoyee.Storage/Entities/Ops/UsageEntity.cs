namespace Shoyee.Storage.Entities.Ops;

/// <summary>单次模型调用用量。</summary>
public sealed class UsageEntity
{
    /// <summary>自增 id。</summary>
    public long Id { get; set; }

    /// <summary>所属会话（可空）。</summary>
    public string? SessionId { get; set; }

    /// <summary>所属轮次（可空）。</summary>
    public string? TurnId { get; set; }

    /// <summary>用途（chat.call = 单次主对话调用 / chat = 整轮聚合 / summary / title / vision / approval / subagent）。</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>模型名。</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>输入 token。</summary>
    public int InputTokens { get; set; }

    /// <summary>输出 token。</summary>
    public int OutputTokens { get; set; }

    /// <summary>缓存命中的输入 token。</summary>
    public int CacheHitTokens { get; set; }

    /// <summary>缓存未命中的输入 token。</summary>
    public int CacheMissTokens { get; set; }

    /// <summary>时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
