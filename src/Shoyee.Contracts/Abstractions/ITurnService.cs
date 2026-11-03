using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>轮次与打断的服务面：渠道、Handlers、Proactive、Gateway 共用，实现在 Agent。</summary>
public interface ITurnService
{
    /// <summary>
    /// 投递一轮用户消息；立即返回轮次 id，正文走事件流。
    /// 工作模式给 <see cref="Dtos.TurnSendRequest.SessionId"/>；RP 没有会话，给 <see cref="Dtos.TurnSendRequest.LaneId"/>。
    /// </summary>
    Task<TurnRef> SendAsync(TurnSendRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 投递一轮并**等它跑完**，返回落库后的正文与消息 id。
    ///
    /// 只给主动场景用（心跳/cron）：它们要拿到正文再决定往哪投递；桌面/IM 的普通对话仍然用
    /// <see cref="SendAsync"/>，正文走事件流，不阻塞调用方。
    /// </summary>
    /// <param name="request">轮次请求。</param>
    /// <param name="cancellationToken">取消令牌（取消 = 停这一轮）。</param>
    Task<TurnOutcome> SendAndWaitAsync(TurnSendRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 往 RP 时间线追加一条「她直接说出的话」：不跑模型、不产生增量事件，
    /// 但和普通轮次一样落 <c>rounds</c>（全局串行）并发 <c>message.appended</c>。
    /// 供定时任务/固定通知这类「内容已经定好」的主动消息使用。
    /// </summary>
    /// <param name="laneId">目标 RP Lane。</param>
    /// <param name="text">要说的正文。</param>
    /// <param name="attachments">随这条消息发出的附件（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TurnOutcome> AppendSpokenAsync(
        string laneId,
        string text,
        IReadOnlyList<Dtos.MessageAttachment>? attachments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重跑该工作会话上一轮**失败/取消**的用户消息：先把失败的输入从历史里退回去，再按原请求发一轮。
    ///
    /// 只在 daemon 进程内记住「上一轮」；重启后没有可重试对象（返回 <c>invalid_params</c>），
    /// 不凭历史猜——失败可能发生在落库之前的任意阶段。RP 不支持（时间线不删）。
    /// </summary>
    Task<TurnRef> RetryAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>打断当前轮（停就停，不回滚已落盘内容）。</summary>
    Task CancelAsync(string sessionId, string? turnId, CancellationToken cancellationToken = default);

    /// <summary>答复待审批请求。</summary>
    Task RespondApprovalAsync(ApprovalRespondRequest request, CancellationToken cancellationToken = default);
}
