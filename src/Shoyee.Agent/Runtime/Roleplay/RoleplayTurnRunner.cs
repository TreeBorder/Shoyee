using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Sessions.Roleplay;
using Shoyee.Sessions.Summary;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Media;
using Shoyee.Tools.Cron;
using Shoyee.Tools.Photo;

namespace Shoyee.Agent.Runtime.Roleplay;

/// <summary>
/// RP 一轮的编排：组装上下文 → 跑 agent → 收响应落库 → 到点触发摘要。
///
/// 每轮新建一个临时 session，跑完即弃；历史只从 <c>rounds</c> 重建，不序列化 session。
/// 摘要模型由调用方以委托传入（本类不决定模型档位）。
///
/// <see cref="RunStreamingAsync"/> 走流式（桌面/RPC 用），<see cref="RunAsync"/> 走整包
/// （后台/心跳等不需要增量外发的场景）；两者落库与摘要逻辑共用。
/// </summary>
public sealed class RoleplayTurnRunner
{
    private readonly ChatClientAgent _agent;
    private readonly RoleplayContextBuilder _contextBuilder;
    private readonly RoundRecorder _recorder;
    private readonly RoleplaySummarizer _summarizer;
    private readonly IMessageRepository? _messages;

    /// <summary>创建 runner。</summary>
    /// <param name="agent">RP agent。</param>
    /// <param name="contextBuilder">五区组装器。</param>
    /// <param name="recorder">轮消息记录器（同一个实例必须挂在 agent 上）。</param>
    /// <param name="summarizer">RP 摘要器（全局一份）。</param>
    /// <param name="messages">消息投影仓库（取 <c>turn.completed.messageId</c>；可空 = 不取）。</param>
    public RoleplayTurnRunner(
        ChatClientAgent agent,
        RoleplayContextBuilder contextBuilder,
        RoundRecorder recorder,
        RoleplaySummarizer summarizer,
        IMessageRepository? messages = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(summarizer);
        _agent = agent;
        _contextBuilder = contextBuilder;
        _recorder = recorder;
        _summarizer = summarizer;
        _messages = messages;
    }

    /// <summary>跑一轮 RP（整包，不外发增量）。</summary>
    /// <param name="laneId">Lane id。</param>
    /// <param name="userText">用户输入原文。</param>
    /// <param name="now">消息时间（本地时间）。</param>
    /// <param name="systemInjections">动态系统段（状态/检索/cron 回执）。</param>
    /// <param name="summarize">摘要模型调用；null = 本轮不触发摘要（仍会照常落库）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库结果；模型没有产生任何响应时返回 null。</returns>
    public async Task<RoleplayTurnResult?> RunAsync(
        string laneId,
        string userText,
        DateTimeOffset now,
        IReadOnlyList<string>? systemInjections = null,
        Func<string, CancellationToken, Task<string>>? summarize = null,
        CancellationToken cancellationToken = default,
        bool systemInput = false,
        IReadOnlyList<MessageAttachment>? attachments = null,
        string? displayText = null)
    {
        var context = await _contextBuilder
            .BuildAsync(laneId, userText, now, systemInjections, cancellationToken)
            .ConfigureAwait(false);

        var session = await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var previousLane = PhotoScope.LaneId;
        var previousCronLane = CronScope.LaneId;
        var previousOutbound = OutboundScope.Current;
        var collector = new OutboundAttachments();
        PhotoScope.LaneId = laneId;
        CronScope.LaneId = laneId;
        OutboundScope.Current = collector;
        try
        {
            var response = await _agent.RunAsync(context.Messages, session, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return await CompleteAsync(
                    laneId,
                    session,
                    StoredInput(context, systemInput, attachments, displayText),
                    response.Usage is null ? null : ToSummary(response.Usage),
                    now,
                    summarize,
                    Snapshot(collector),
                    calls: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            PhotoScope.LaneId = previousLane;
            CronScope.LaneId = previousCronLane;
            OutboundScope.Current = previousOutbound;
        }
    }

    /// <summary>跑一轮 RP（流式：正文增量与工具过程通过 <paramref name="observer"/> 外发）。</summary>
    /// <param name="laneId">Lane id。</param>
    /// <param name="userText">用户输入原文。</param>
    /// <param name="now">消息时间（本地时间）。</param>
    /// <param name="observer">增量/过程观察者。</param>
    /// <param name="systemInjections">动态系统段。</param>
    /// <param name="approvalHandler">审批回调；null = 拒绝所有待审批调用。</param>
    /// <param name="summarize">摘要模型调用；null = 本轮不触发摘要。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="systemInput">本轮输入是系统注入。</param>
    /// <param name="attachments">入站附件。</param>
    /// <param name="displayText">给人看的正文；null = 用 <paramref name="userText"/>。</param>
    /// <param name="softInterrupt">软打断查询（步边界生效）；null = 不响应。</param>
    public async Task<RoleplayTurnResult?> RunStreamingAsync(
        string laneId,
        string userText,
        DateTimeOffset now,
        TurnObserver observer,
        IReadOnlyList<string>? systemInjections = null,
        ApprovalHandler? approvalHandler = null,
        Func<string, CancellationToken, Task<string>>? summarize = null,
        CancellationToken cancellationToken = default,
        bool systemInput = false,
        IReadOnlyList<MessageAttachment>? attachments = null,
        string? displayText = null,
        Func<bool>? softInterrupt = null)
    {
        ArgumentNullException.ThrowIfNull(observer);

        var context = await _contextBuilder
            .BuildAsync(laneId, userText, now, systemInjections, cancellationToken)
            .ConfigureAwait(false);

        var session = await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        TurnUsage usage;
        var previousLane = PhotoScope.LaneId;
        var previousCronLane = CronScope.LaneId;
        var previousOutbound = OutboundScope.Current;
        var collector = new OutboundAttachments();
        IReadOnlyList<MessageAttachment>? outbound = null;
        PhotoScope.LaneId = laneId;
        CronScope.LaneId = laneId;
        OutboundScope.Current = collector;
        try
        {
            var result = await AgentStreamRunner.RunAsync(
                    _agent, context.Messages, session, null, observer, approvalHandler, cancellationToken, softInterrupt)
                .ConfigureAwait(false);
            usage = result.Usage;
            outbound = Snapshot(collector);
        }
        catch (OperationCanceledException)
        {
            // 停就停：本次已产生的响应照常落库（修补悬空调用后），不因为取消丢掉说过的话
            await CompleteAsync(
                    laneId,
                    session,
                    StoredInput(context, systemInput, attachments, displayText),
                    null,
                    now,
                    summarize: null,
                    Snapshot(collector),
                    calls: null,
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            PhotoScope.LaneId = previousLane;
            CronScope.LaneId = previousCronLane;
            OutboundScope.Current = previousOutbound;
        }

        return await CompleteAsync(
                laneId,
                session,
                StoredInput(context, systemInput, attachments, displayText),
                usage.ToSummary(),
                now,
                summarize,
                outbound,
                usage.CallDetails.ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>落库的输入消息；系统注入打标记，投影层据此不写可见消息。</summary>
    private static ChatMessage StoredInput(
        RoleplayContext context,
        bool systemInput,
        IReadOnlyList<MessageAttachment>? attachments,
        string? displayText)
    {
        var message = InputAttachments.Tag(
            context.StoredInput,
            displayText ?? context.StoredInput.Text ?? string.Empty,
            attachments);
        if (!systemInput)
        {
            return message;
        }

        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[ChatMessageMarkers.SystemInput] = true;
        return message;
    }

    private async Task<RoleplayTurnResult?> CompleteAsync(
        string laneId,
        AgentSession session,
        ChatMessage input,
        UsageSummary? usage,
        DateTimeOffset now,
        Func<string, CancellationToken, Task<string>>? summarize,
        IReadOnlyList<MessageAttachment>? outbound,
        IReadOnlyList<UsageCall>? calls,
        CancellationToken cancellationToken)
    {
        var round = await _recorder.CompleteRoundAsync(laneId, session, input, outbound, cancellationToken)
            .ConfigureAwait(false);
        if (round is null)
        {
            return null;
        }

        if (summarize is not null)
        {
            await _summarizer.TrySummarizeAsync(summarize, now, cancellationToken).ConfigureAwait(false);
        }

        long? messageId = null;
        if (_messages is not null)
        {
            var row = await _messages
                .GetLastAssistantAsync(laneId: laneId, roundNo: round.RoundNo, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            messageId = row?.Id;
        }

        return new RoleplayTurnResult(round, round.Response, usage, messageId, outbound, calls);
    }

    /// <summary>取本轮收集到的附件（没有就 null，避免到处传空列表）。</summary>
    private static IReadOnlyList<MessageAttachment>? Snapshot(OutboundAttachments collector)
        => collector.Items.Count == 0 ? null : collector.Items.ToArray();

    private static UsageSummary ToSummary(UsageDetails usage) => new(
        1,
        usage.InputTokenCount is { } input ? (int)input : 0,
        usage.OutputTokenCount is { } output ? (int)output : 0,
        usage.CachedInputTokenCount is { } cached ? (int)cached : 0,
        Math.Max(0, (int)((usage.InputTokenCount ?? 0) - (usage.CachedInputTokenCount ?? 0))));
}
