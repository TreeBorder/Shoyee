using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Entities.Channel;

/// <summary>投递台账：挂账、补发、销账。</summary>
public sealed class DeliveryEntity
{
    /// <summary>自增 id。</summary>
    public long Id { get; set; }

    /// <summary>目标 Lane。</summary>
    public string LaneId { get; set; } = string.Empty;

    /// <summary>所属会话（可空）。</summary>
    public string? SessionId { get; set; }

    /// <summary>种类（message / file / heartbeat / notify）。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>负载 JSON。</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>状态。</summary>
    public DeliveryState State { get; set; }

    /// <summary>已尝试次数。</summary>
    public int Attempts { get; set; }

    /// <summary>下次重试时间（UTC）。</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>最近错误。</summary>
    public string? LastError { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
