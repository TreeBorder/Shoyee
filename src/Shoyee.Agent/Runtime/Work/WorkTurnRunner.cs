using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Agent.Composition;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Sessions.Persistence;
using Shoyee.Sessions.Turns;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Media;
using Shoyee.Tools.Process;

namespace Shoyee.Agent.Runtime.Work;

/// <summary>一次工作回合的结果。</summary>
/// <param name="Text">本轮可见正文全文。</param>
/// <param name="Usage">本轮模型用量。</param>
/// <param name="MessageId">本轮落库的助手消息 id（投影真值；没有可见正文时为 null）。</param>
/// <param name="Attachments">本轮她说出口的附件（已随助手消息落库）。</param>
/// <param name="Calls">本轮每次模型调用的用量明细（工具循环会多次；端点不报时为空）。</param>
public sealed record WorkTurnResult(
    string Text,
    UsageSummary Usage,
    long? MessageId = null,
    IReadOnlyList<MessageAttachment>? Attachments = null,
    IReadOnlyList<UsageCall>? Calls = null);

/// <summary>
/// 工作模式一轮的编排：跑 agent → 修补悬空工具调用 → 整包落盘。
///
/// 同一会话串行执行；**跑到中途用的是内存里的同一个 session 对象，不序列化**；
/// 落盘只在本回合结束时发生一次（放在 finally，中断/失败也保留已发生的上下文，修补后再存）。
/// 不负责建会话——首次会话由 <see cref="WorkAgentFactory.CreateSessionAsync"/> 建好后放进缓存。
/// </summary>
public sealed class WorkTurnRunner
{
    private readonly HarnessAgent _agent;
    private readonly WorkAgentFactory _factory;
    private readonly WorkSessionCache _cache;
    private readonly SessionTranscript? _transcript;
    private readonly ILogger<WorkTurnRunner> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <summary>创建 runner。</summary>
    /// <param name="agent">工作模式 agent。</param>
    /// <param name="factory">agent 工厂（用来修补历史、读历史做转录投影）。</param>
    /// <param name="cache">常驻 session 缓存。</param>
    /// <param name="transcript">转录投影器；null = 不投影（测试/无存储场景）。</param>
    /// <param name="logger">日志。</param>
    public WorkTurnRunner(
        HarnessAgent agent,
        WorkAgentFactory factory,
        WorkSessionCache cache,
        SessionTranscript? transcript = null,
        ILogger<WorkTurnRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(cache);
        _agent = agent;
        _factory = factory;
        _cache = cache;
        _transcript = transcript;
        _logger = logger ?? NullLogger<WorkTurnRunner>.Instance;
    }

    /// <summary>跑一轮（整包，不外发增量）。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="laneId">会话所属 Lane（转录投影的归属）。</param>
    /// <param name="input">本轮用户消息。</param>
    /// <param name="options">本轮 ChatOptions（工具按请求组装时从这里进）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AgentResponse> RunAsync(
        string sessionId,
        string laneId,
        ChatMessage input,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentNullException.ThrowIfNull(input);

        var gate = _gates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await _cache.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var beforeCount = _factory.GetHistory(session).Count;
            var collector = new OutboundAttachments();
            var previousScope = ProcessScope.SessionKey;
            var previousOutbound = OutboundScope.Current;
            ProcessScope.SessionKey = sessionId;
            OutboundScope.Current = collector;
            try
            {
                // 新回合开跑前：补上一轮可能留下的悬空工具调用、清框架审批残留（否则下一次请求 400）
                _factory.PrepareTurn(session);

                return await _agent
                    .RunAsync([input], session, new ChatClientAgentRunOptions(options), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ProcessScope.SessionKey = previousScope;
                OutboundScope.Current = previousOutbound;
                // 中断时历史里可能留着「有调用、无结果」；先修再存，resume 才不会 400
                _factory.RepairHistory(session);
                _factory.ClearApprovalResidue(session);
                AttachOutbound(session, collector, beforeCount);
                await _cache.SaveAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                await ProjectAsync(laneId, sessionId, session).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>跑一轮（流式：正文增量与工具过程通过 <paramref name="observer"/> 外发，支持审批续跑）。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="laneId">会话所属 Lane（转录投影的归属）。</param>
    /// <param name="input">本轮用户消息。</param>
    /// <param name="observer">增量/过程观察者。</param>
    /// <param name="options">本轮 ChatOptions。</param>
    /// <param name="approvalHandler">审批回调；null = 拒绝所有待审批调用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="softInterrupt">软打断查询（步边界生效）；null = 不响应。</param>
    public async Task<WorkTurnResult> RunStreamingAsync(
        string sessionId,
        string laneId,
        ChatMessage input,
        TurnObserver observer,
        ChatOptions? options = null,
        ApprovalHandler? approvalHandler = null,
        CancellationToken cancellationToken = default,
        Func<bool>? softInterrupt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(observer);

        var gate = _gates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await _cache.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var beforeCount = _factory.GetHistory(session).Count;
            WorkTurnResult? result = null;
            IReadOnlyList<MessageRecord> projected = [];
            var collector = new OutboundAttachments();
            var previousScope = ProcessScope.SessionKey;
            var previousOutbound = OutboundScope.Current;
            ProcessScope.SessionKey = sessionId;
            OutboundScope.Current = collector;
            try
            {
                // 新回合开跑前：补上一轮可能留下的悬空工具调用、清框架审批残留（否则下一次请求 400）
                _factory.PrepareTurn(session);

                var run = await AgentStreamRunner.RunAsync(
                        _agent,
                        [input],
                        session,
                        new ChatClientAgentRunOptions(options),
                        observer,
                        approvalHandler,
                        cancellationToken,
                        softInterrupt)
                    .ConfigureAwait(false);
                result = new WorkTurnResult(
                    run.Text,
                    run.Usage.ToSummary(),
                    Calls: run.Usage.CallDetails.ToArray());
            }
            finally
            {
                ProcessScope.SessionKey = previousScope;
                OutboundScope.Current = previousOutbound;
                // 中断/失败也保留已发生的上下文：修补悬空调用、清审批残留再整包落盘
                _factory.RepairHistory(session);
                _factory.ClearApprovalResidue(session);
                AttachOutbound(session, collector, beforeCount);
                await _cache.SaveAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                projected = await ProjectAsync(laneId, sessionId, session).ConfigureAwait(false);
            }

            return result! with
            {
                MessageId = LastAssistantId(projected),
                Attachments = Snapshot(collector),
            };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 重试前把上一轮失败的输入从历史里退回去：找到最后一条用户消息，
    /// 确认它的可见正文与 <paramref name="displayText"/> 一致后，整段截断到它之前并重抄投影。
    ///
    /// 找不到（失败发生在写入历史之前 / 队列里被取消）返回 false，调用方按「历史里没有残留」继续。
    /// </summary>
    /// <param name="sessionId">工作会话 id。</param>
    /// <param name="laneId">会话所属 Lane（重建投影用）。</param>
    /// <param name="displayText">上一轮用户输入的可见正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<bool> RewindLastInputAsync(
        string sessionId,
        string laneId,
        string displayText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);

        var gate = _gates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await _cache.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var history = _factory.GetHistory(session).ToList();
            var index = history.FindLastIndex(static message => message.Role == ChatRole.User);
            if (index < 0 || !string.Equals(DisplayTextOf(history[index]), displayText, StringComparison.Ordinal))
            {
                return false;
            }

            var trimmed = history.Take(index).ToList();
            _factory.SetHistory(session, trimmed);
            await _cache.SaveAsync(sessionId, CancellationToken.None).ConfigureAwait(false);

            if (_transcript is not null)
            {
                await _transcript
                    .RebuildAsync(laneId, sessionId, trimmed, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>用户输入的可见正文：有 <c>_display_text</c> 用标记，没有就用模型看到的正文。</summary>
    private static string DisplayTextOf(ChatMessage message)
    {
        if (message.AdditionalProperties is { } properties
            && properties.TryGetValue(ChatMessageMarkers.DisplayText, out var value))
        {
            return value switch
            {
                string text => text,
                System.Text.Json.JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.String
                    => element.GetString() ?? string.Empty,
                null => message.Text,
                _ => value.ToString() ?? string.Empty,
            };
        }

        return message.Text;
    }

    /// <summary>
    /// 落盘后补抄转录投影。失败只记警告：投影是派生数据，不能反过来让一轮对话失败。
    /// </summary>
    private async Task<IReadOnlyList<MessageRecord>> ProjectAsync(
        string laneId,
        string sessionId,
        AgentSession session)
    {
        if (_transcript is null)
        {
            return [];
        }

        try
        {
            var history = _factory.GetHistory(session);
            return await _transcript.SyncAsync(laneId, sessionId, history, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "转录投影失败：{SessionId}（{LaneId}）", sessionId, laneId);
            return [];
        }
    }

    /// <summary>本轮新增投影里最后一条可见助手正文——<c>turn.completed.messageId</c> 指向它。</summary>
    private static long? LastAssistantId(IReadOnlyList<MessageRecord> projected)
        => projected
            .LastOrDefault(static row => row.Role == "assistant"
                                         && row.Content.Length > 0
                                         && row.Kind is MessageKind.Text or MessageKind.Spoken)
            ?.Id;

    /// <summary>把本轮收集到的出站附件挂到最后一条助手正文上（只在本轮新增的段落里找）。</summary>
    private void AttachOutbound(AgentSession session, OutboundAttachments collector, int beforeCount)
    {
        if (collector.Items.Count == 0)
        {
            return;
        }

        TurnAttachments.AttachToLastAssistantText(
            _factory.GetHistory(session),
            collector.Items.ToArray(),
            beforeCount);
    }

    private static IReadOnlyList<MessageAttachment>? Snapshot(OutboundAttachments collector)
        => collector.Items.Count == 0 ? null : collector.Items.ToArray();
}
