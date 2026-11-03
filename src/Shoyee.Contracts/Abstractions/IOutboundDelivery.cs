namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 主动消息的文本出口（心跳、通知等不走入站轮次的消息用它）。
///
/// 与 <see cref="IChannelSender"/> 的区别：这里按 Lane 投递、走投递台账（失败补发），
/// 调用方只给「发到哪条 Lane 的哪个回投地址」，不需要知道具体是 Mattermost 还是微信。
/// </summary>
public interface IOutboundDelivery
{
    /// <summary>发一条文本；失败挂账待补发。</summary>
    /// <param name="laneId">目标 Lane。</param>
    /// <param name="sessionId">所属会话（可空；RP 传 null）。</param>
    /// <param name="target">回投地址（Lane 的 <c>last_target</c>）。</param>
    /// <param name="text">正文。</param>
    /// <param name="kind">台账种类（message / heartbeat / notify）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendTextAsync(
        string laneId,
        string? sessionId,
        string target,
        string text,
        string kind = "message",
        CancellationToken cancellationToken = default);

    /// <summary>发一个本地文件（主动场景的语音/图片）；失败挂账待补发。</summary>
    /// <param name="laneId">目标 Lane。</param>
    /// <param name="sessionId">所属会话（可空；RP 传 null）。</param>
    /// <param name="target">回投地址（Lane 的 <c>last_target</c>）。</param>
    /// <param name="filePath">本地绝对路径。</param>
    /// <param name="caption">配文（可空）。</param>
    /// <param name="kind">台账种类（file / heartbeat / notify）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendFileAsync(
        string laneId,
        string? sessionId,
        string target,
        string filePath,
        string? caption = null,
        string kind = "file",
        CancellationToken cancellationToken = default);
}
