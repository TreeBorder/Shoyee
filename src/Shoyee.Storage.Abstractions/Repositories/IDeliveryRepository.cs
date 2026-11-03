namespace Shoyee.Storage.Abstractions;

/// <summary>
/// ⚠️ 预置仓储：暂无调用方，接口可能随对应模块（Agent 审批 / Channels / Proactive）调整。
/// </summary>
/// <summary>投递台账仓库（挂账、补发、销账）。</summary>
public interface IDeliveryRepository
{
    /// <summary>挂账；同 Lane + 同种类 + 同负载的 pending 复用（幂等）。</summary>
    Task<DeliveryRecord> EnqueueAsync(
        string laneId,
        string? sessionId,
        string kind,
        string payloadJson,
        CancellationToken cancellationToken = default);

    /// <summary>取到期的 pending（按下次重试时间升序）。</summary>
    Task<IReadOnlyList<DeliveryRecord>> GetDueAsync(DateTimeOffset now, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取该 Lane 待发/失败的记录（**无视退避时间**）。
    /// 渠道的发送窗口刚打开时用：窗口外挂着的消息不必等到退避到点。
    /// </summary>
    Task<IReadOnlyList<DeliveryRecord>> GetPendingForLaneAsync(
        string laneId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>标记送达。</summary>
    Task MarkSentAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>标记失败并安排下次重试；超过上限则放弃。</summary>
    Task MarkFailedAsync(
        long id,
        string error,
        DateTimeOffset nextAttemptAt,
        int maxAttempts = 10,
        CancellationToken cancellationToken = default);
}
