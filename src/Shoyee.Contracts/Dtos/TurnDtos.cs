using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Dtos;

/// <summary>轮次标识。</summary>
/// <param name="TurnId">轮次 id。</param>
public sealed record TurnRef(string TurnId);

/// <summary>
/// 一轮跑完的结果（主动调用方用：心跳/cron 需要拿到落库后的正文再决定投递）。
///
/// <see cref="Text"/> 是流式增量的拼接，和桌面上看到的一致；失败/取消时可能只有半截。
/// </summary>
/// <param name="TurnId">轮次 id。</param>
/// <param name="Succeeded">是否正常跑完（失败/取消为 false）。</param>
/// <param name="Text">本轮可见正文（可能为空）。</param>
/// <param name="MessageId">落库的助手消息 id（没有正文时为 null）。</param>
/// <param name="Attachments">本轮她说出口的附件（主动场景要自己投递）。</param>
public sealed record TurnOutcome(
    string TurnId,
    bool Succeeded,
    string Text,
    long? MessageId = null,
    IReadOnlyList<MessageAttachment>? Attachments = null);

/// <summary>
/// 发送一轮的请求。
///
/// 工作模式给 <paramref name="SessionId"/>；RP 没有会话，给 <paramref name="LaneId"/>（如 <c>desktop:roleplay</c>）。
/// 两个都给时以 <paramref name="SessionId"/> 为准（会话本身就是 Lane 的归属）。
/// </summary>
/// <param name="Text">正文。</param>
/// <param name="SessionId">工作会话 id；RP 为 null。</param>
/// <param name="LaneId">Lane id；RP 必填，工作模式可省略（从会话记录取）。</param>
/// <param name="Attachments">附件（入站附件随本轮一起进模型上下文与消息投影）。</param>
/// <param name="ReplyTarget">
/// 回投地址（可选）：这一轮的最终正文要回到哪。IM 入站必须带（就是入站消息的 <c>Target</c>）；
/// 桌面/主动消息不带——桌面走事件，主动消息由发起方自己决定投递。
/// </param>
/// <param name="SystemInput">
/// 本轮输入是系统注入（心跳/进程回执/定时任务）而不是用户说的话：它会进模型上下文，
/// 投影成 <see cref="Domain.MessageKind.SystemInput"/>——桌面默认隐藏，可在「系统消息」开关里查看；不检索。
/// </param>
public sealed record TurnSendRequest(
    string Text,
    string? SessionId = null,
    string? LaneId = null,
    IReadOnlyList<MessageAttachment>? Attachments = null,
    string? ReplyTarget = null,
    bool SystemInput = false);

/// <summary>答复审批的请求。</summary>
/// <param name="ApprovalId">审批 id。</param>
/// <param name="Decision">结论（approved / denied / always）。</param>
/// <param name="Note">备注（可选）。</param>
public sealed record ApprovalRespondRequest(
    string ApprovalId,
    ApprovalDecision Decision,
    string? Note = null);

/// <summary><c>turn.cancel</c> 的请求。</summary>
/// <param name="SessionId">会话 id；RP 用 Lane id 占位。</param>
/// <param name="TurnId">要取消的轮次；null = 该会话当前轮。</param>
public sealed record TurnCancelRequest(string SessionId, string? TurnId = null);

/// <summary>
/// <c>turn.retry</c> 的请求：重跑该会话上一轮**失败/取消**的用户消息。
///
/// 只支持工作会话：RP 的时间线是最终产物，失败时已经落库的半轮不删，
/// 重说一遍是新的轮次，不该伪装成「重试」。
/// </summary>
/// <param name="SessionId">工作会话 id。</param>
public sealed record TurnRetryRequest(string SessionId);


/// <summary><c>turn.started</c> 载荷。</summary>
/// <param name="TurnId">轮次 id。</param>
/// <param name="SessionId">会话 id；RP 用 Lane id 占位（见 <paramref name="LaneId"/>）。</param>
/// <param name="LaneId">所属 Lane（两种模式都有，用于可见性过滤）。</param>
/// <param name="ReplyTarget">回投地址（IM 入站轮次才有；渠道回帖据此对位）。</param>
public sealed record TurnStartedEvent(
    string TurnId,
    string SessionId,
    string? LaneId = null,
    string? ReplyTarget = null);

/// <summary><c>turn.delta</c> 载荷（纯增量文本）。</summary>
/// <param name="TurnId">轮次 id。</param>
/// <param name="SessionId">会话 id；RP 用 Lane id 占位。</param>
/// <param name="Delta">增量文本。</param>
/// <param name="LaneId">所属 Lane。</param>
public sealed record TurnDeltaEvent(string TurnId, string SessionId, string Delta, string? LaneId = null);

/// <summary><c>turn.thinking</c> 载荷（reasoning 增量；客户端自己拼，不进正文）。</summary>
/// <param name="TurnId">轮次 id。</param>
/// <param name="SessionId">会话 id；RP 用 Lane id 占位。</param>
/// <param name="Delta">增量思考文本。</param>
/// <param name="LaneId">所属 Lane。</param>
public sealed record TurnThinkingEvent(string TurnId, string SessionId, string Delta, string? LaneId = null);

/// <summary><c>turn.tool</c> 载荷。</summary>
/// <param name="TurnId">轮次 id。</param>
/// <param name="SessionId">会话 id；RP 用 Lane id 占位。</param>
/// <param name="Phase">start / end。</param>
/// <param name="ToolName">工具名。</param>
/// <param name="Summary">给过程行的一句话摘要。</param>
/// <param name="LaneId">所属 Lane。</param>
/// <param name="CallId">模型给的调用 id（调用行与结果行配对用）。</param>
/// <param name="Arguments">参数（start 行；command 保留裸原文，其余 JSON 文本）。</param>
/// <param name="Result">返回（end 行；文本化并截断）。</param>
public sealed record TurnToolEvent(
    string TurnId,
    string SessionId,
    string Phase,
    string ToolName,
    string? Summary,
    string? LaneId = null,
    string? CallId = null,
    string? Arguments = null,
    string? Result = null);

/// <summary><c>turn.completed</c> 载荷。</summary>
/// <param name="TurnId">轮次 id。</param>
/// <param name="SessionId">会话 id；RP 用 Lane id 占位。</param>
/// <param name="MessageId">落库的助手消息 id；投影尚未接入时为 null。</param>
/// <param name="Usage">本轮模型用量。</param>
/// <param name="LaneId">所属 Lane。</param>
/// <param name="ReplyTarget">回投地址（IM 入站轮次才有）。</param>
/// <param name="Attachments">
/// 本轮她说出口的附件（语音/图片/显式发送的文件）。桌面从事件就能直接渲染，
/// IM 侧由回帖旁路按顺序传文件；没有附件为 null。
/// </param>
/// <param name="Context">
/// 本轮最后一次主对话调用的上下文占用；端点不报 usage 时为 null。
/// </param>
public sealed record TurnCompletedEvent(
    string TurnId,
    string SessionId,
    long? MessageId,
    UsageSummary Usage,
    string? LaneId = null,
    string? ReplyTarget = null,
    IReadOnlyList<MessageAttachment>? Attachments = null,
    ContextUsage? Context = null);

/// <summary><c>turn.failed</c> 载荷。</summary>
/// <param name="TurnId">轮次 id。</param>
/// <param name="SessionId">会话 id；RP 用 Lane id 占位。</param>
/// <param name="Code">结局代码（<see cref="Rpc.TurnFailureCodes"/>）。</param>
/// <param name="Message">给人看的说明。</param>
/// <param name="LaneId">所属 Lane。</param>
/// <param name="ReplyTarget">回投地址（IM 入站轮次才有）。</param>
public sealed record TurnFailedEvent(
    string TurnId,
    string SessionId,
    string Code,
    string Message,
    string? LaneId = null,
    string? ReplyTarget = null);

/// <summary><c>message.appended</c> 载荷。</summary>
/// <param name="Message">消息展示形态。</param>
/// <param name="LaneId">所属 Lane（可见性过滤用；渠道阶段起必填）。</param>
public sealed record MessageAppendedEvent(MessageView Message, string? LaneId = null);
