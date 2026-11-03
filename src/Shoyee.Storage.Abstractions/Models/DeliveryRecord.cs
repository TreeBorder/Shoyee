using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Abstractions;

/// <summary>一条投递台账。</summary>
/// <param name="Id">自增 id。</param>
/// <param name="LaneId">目标 Lane。</param>
/// <param name="SessionId">所属会话。</param>
/// <param name="Kind">种类（message / file / heartbeat / notify）。</param>
/// <param name="PayloadJson">负载 JSON。</param>
/// <param name="State">状态。</param>
/// <param name="Attempts">已尝试次数。</param>
/// <param name="NextAttemptAt">下次重试时间。</param>
/// <param name="LastError">最近错误。</param>
/// <param name="CreatedAt">创建时间。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record DeliveryRecord(
    long Id,
    string LaneId,
    string? SessionId,
    string Kind,
    string PayloadJson,
    DeliveryState State,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
