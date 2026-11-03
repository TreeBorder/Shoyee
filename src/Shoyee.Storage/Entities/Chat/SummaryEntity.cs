namespace Shoyee.Storage.Entities.Chat;

/// <summary>RP 摘要（全局累积全文）。工作会话压缩不写这张表。</summary>
public sealed class SummaryEntity
{
    /// <summary>自增 id。</summary>
    public long Id { get; set; }

    /// <summary>作用域（v1 恒为 rp）。</summary>
    public string Scope { get; set; } = "rp";

    /// <summary>目标 id（v1 恒为 roleplay）。</summary>
    public string TargetId { get; set; } = "roleplay";

    /// <summary>已覆盖到的全局轮次号。</summary>
    public int ThroughRound { get; set; }

    /// <summary>本次覆盖的轮数。</summary>
    public int RoundCount { get; set; }

    /// <summary>累积摘要全文。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
