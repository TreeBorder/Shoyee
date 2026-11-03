using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Abstractions;

/// <summary>一条 Lane。</summary>
/// <param name="Id">Lane id。</param>
/// <param name="Channel">渠道类型。</param>
/// <param name="Mode">绑定模式。</param>
/// <param name="ExternalKey">渠道侧标识。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="ActiveSessionId">当前工作会话指针。</param>
/// <param name="LastInboundAt">最近入站时间。</param>
/// <param name="LastInteractionAt">最近互动时间（跨模式跨渠道）。</param>
/// <param name="CreatedAt">创建时间。</param>
/// <param name="LastTarget">最近一次入站消息的回投地址（主动投递用；桌面 Lane 恒为 null）。</param>
public sealed record LaneRecord(
    string Id,
    LaneChannel Channel,
    Mode Mode,
    string ExternalKey,
    string? DisplayName,
    string? ActiveSessionId,
    DateTimeOffset? LastInboundAt,
    DateTimeOffset? LastInteractionAt,
    DateTimeOffset CreatedAt,
    string? LastTarget = null);
