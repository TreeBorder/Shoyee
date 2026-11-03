namespace Shoyee.Storage.Abstractions;

/// <summary>一条通用状态（照 Harem <c>StateItem</c>）：新增状态只加行，不改代码。</summary>
/// <param name="Key">稳定键（如 <c>longing</c> / <c>mood</c>）。</param>
/// <param name="Name">展示名。</param>
/// <param name="Value">统一用字符串存；数值由消费者解析。</param>
/// <param name="Description">给模型的说明与取值约束。</param>
/// <param name="AgentEditable">她是否可以自己改。</param>
/// <param name="UpdatedAt">更新时间（UTC）。</param>
public sealed record StateRecord(
    string Key,
    string Name,
    string Value,
    string? Description,
    bool AgentEditable,
    DateTimeOffset UpdatedAt);
