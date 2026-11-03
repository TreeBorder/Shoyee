using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Entities.Channel;

/// <summary>Lane = 一个渠道账号（桌面也是一条）。</summary>
public sealed class LaneEntity
{
    /// <summary>Lane id（见 <c>LaneIds</c>）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>渠道类型。</summary>
    public LaneChannel Channel { get; set; }

    /// <summary>绑定的模式。</summary>
    public Mode Mode { get; set; }

    /// <summary>渠道侧标识（用于入站匹配）。</summary>
    public string ExternalKey { get; set; } = string.Empty;

    /// <summary>展示名。</summary>
    public string? DisplayName { get; set; }

    /// <summary>该渠道当前会话指针（仅工作模式）。</summary>
    public string? ActiveSessionId { get; set; }

    /// <summary>最近入站时间（UTC）。</summary>
    public DateTimeOffset? LastInboundAt { get; set; }

    /// <summary>最近一次互动时间（UTC，跨模式跨渠道；心跳/思念的唯一依据）。</summary>
    public DateTimeOffset? LastInteractionAt { get; set; }

    /// <summary>最近一次入站消息的回投地址（主动投递用；桌面 Lane 恒为 null）。</summary>
    public string? LastTarget { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
