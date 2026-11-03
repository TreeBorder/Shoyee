using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Agent.Approval;
using Shoyee.Agent.Media;
using Shoyee.Agent.Models;
using Shoyee.Agent.Runtime.Roleplay;
using Shoyee.Agent.Runtime.Work;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Sessions.Lifecycle;
using Shoyee.Sessions.Turns;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Runtime;

/// <summary>
/// <see cref="ITurnService"/> 的实现：渠道 / Handlers / Gateway 共用的唯一轮次入口。
///
/// - <see cref="SendAsync"/> 只负责验收、排队、发 <c>turn.started</c> 后立即返回轮次 id，正文走事件；
/// - 同一会话串行：工作模式按会话、RP 按**整条时间线**（跨 Lane 全局串行）；后来的轮次排队；
/// - 有待审批请求时拒绝新轮次（<c>-32008</c>），避免把挂起的工具调用踩成悬空；
/// - 打断 = 取消本轮的 <see cref="CancellationTokenSource"/>：已落盘/已产生的内容不回滚；
/// - 模型用量按轮次汇总，写 <c>usage</c> 表并随 <c>turn.completed</c> 发出。
/// </summary>
public sealed class TurnService : ITurnService, ITurnDrain
{
    private readonly ISessionRepository _sessions;
    private readonly ILaneRepository _lanes;
    private readonly RoleplayTurnRunner _roleplay;
    private readonly WorkTurnRunner _work;
    private readonly ApprovalBroker _approvals;
    private readonly ITurnEventSink _events;
    private readonly IUsageRepository? _usage;
    private readonly ModelClientFactory? _models;
    private readonly WorkspaceConfig? _config;
    private readonly IAttachmentAnalyzer? _analyzer;
    private readonly IRoundRepository? _rounds;
    private readonly IMessageRepository? _messages;
    private readonly ILogger<TurnService> _logger;
    private readonly TimeProvider _clock;
    private readonly Func<string> _newId;

    private const string RoleplayGateKey = "mode:roleplay";

    private readonly ConcurrentDictionary<string, ActiveTurn> _turns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _currentTurnByKey = new(StringComparer.Ordinal);

    /// <summary>串行队列的锁：同一会话同一时刻只有一个在跑，后来的合并进一个待跑槽。</summary>
    private readonly object _queueGate = new();
    private readonly Dictionary<string, ActiveTurn> _activeByKey = new(StringComparer.Ordinal);
    /// <summary>每个会话的待跑槽：忙时到达的消息合并成**一轮**，不是排成一串。</summary>
    private readonly Dictionary<string, ActiveTurn> _pendingByKey = new(StringComparer.Ordinal);
    /// <summary>每个会话是否已经试过自动起名（失败会移除，下一轮再试）。</summary>
    private readonly ConcurrentDictionary<string, byte> _titleAttempts = new(StringComparer.Ordinal);
    /// <summary>每个会话上一轮的请求与结局；只用于 <c>turn.retry</c>（进程内，重启不保留）。</summary>
    private readonly ConcurrentDictionary<string, LastTurn> _lastTurns = new(StringComparer.Ordinal);
    private readonly Lazy<IChatClient?> _smallClient;
    private volatile TaskCompletionSource _idleSignal = NewIdleSignal();

    /// <summary>创建轮次服务。</summary>
    /// <param name="sessions">会话仓库。</param>
    /// <param name="lanes">Lane 仓库（RP 按 Lane 找模式）。</param>
    /// <param name="roleplay">RP 轮次 runner。</param>
    /// <param name="work">工作轮次 runner。</param>
    /// <param name="approvals">审批台。</param>
    /// <param name="events">事件出口。</param>
    /// <param name="usage">用量仓库（可空）。</param>
    /// <param name="models">模型工厂（只为 RP 摘要拿小模型；可空 = 不摘要）。</param>
    /// <param name="config">工作区配置（取模型名；可空）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时间源（测试可注入）。</param>
    /// <param name="newId">轮次 id 生成器（测试可注入）。</param>
    /// <param name="analyzer">入站附件自动分析（可空 = 不分析，只把路径交给模型）。</param>
    /// <param name="rounds">RP 轮次仓库（<see cref="AppendSpokenAsync"/> 用；可空 = 该能力不可用）。</param>
    /// <param name="messages">消息投影仓库（<see cref="AppendSpokenAsync"/> 取 messageId / 事件；可空）。</param>
    public TurnService(
        ISessionRepository sessions,
        ILaneRepository lanes,
        RoleplayTurnRunner roleplay,
        WorkTurnRunner work,
        ApprovalBroker approvals,
        ITurnEventSink events,
        IUsageRepository? usage = null,
        ModelClientFactory? models = null,
        WorkspaceConfig? config = null,
        ILogger<TurnService>? logger = null,
        TimeProvider? clock = null,
        Func<string>? newId = null,
        IAttachmentAnalyzer? analyzer = null,
        IRoundRepository? rounds = null,
        IMessageRepository? messages = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(roleplay);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentNullException.ThrowIfNull(events);
        _sessions = sessions;
        _lanes = lanes;
        _roleplay = roleplay;
        _work = work;
        _approvals = approvals;
        _events = events;
        _usage = usage;
        _models = models;
        _config = config;
        _analyzer = analyzer;
        _rounds = rounds;
        _messages = messages;
        _logger = logger ?? NullLogger<TurnService>.Instance;
        _clock = clock ?? TimeProvider.System;
        _newId = newId ?? (static () => "trn_" + Guid.NewGuid().ToString("N"));
        _smallClient = new Lazy<IChatClient?>(CreateSmallClient);
    }

    /// <inheritdoc />
    public async Task<TurnRef> SendAsync(TurnSendRequest request, CancellationToken cancellationToken = default)
    {
        var (turn, shouldRun) = await StartAsync(request, cancellationToken).ConfigureAwait(false);
        if (shouldRun)
        {
            _ = RunAsync(turn);
        }

        return new TurnRef(turn.TurnId);
    }

    /// <inheritdoc />
    public async Task<TurnOutcome> SendAndWaitAsync(
        TurnSendRequest request,
        CancellationToken cancellationToken = default)
    {
        var (turn, shouldRun) = await StartAsync(request, cancellationToken).ConfigureAwait(false);
        if (shouldRun)
        {
            _ = RunAsync(turn);
        }

        return await turn.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TurnOutcome> AppendSpokenAsync(
        string laneId,
        string text,
        IReadOnlyList<MessageAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (_rounds is null)
        {
            throw new RpcException(RpcErrors.Internal, "轮次服务没有接 RP 轮次仓库，无法写固定消息");
        }

        var lane = await _lanes.GetAsync(laneId, cancellationToken).ConfigureAwait(false)
                   ?? throw new RpcException(RpcErrors.NotFound, "Lane 不存在", new { laneId });
        if (lane.Mode != Mode.Roleplay)
        {
            throw new RpcException(RpcErrors.InvalidParams, "只有 RP Lane 支持固定消息", new { laneId });
        }

        // 和普通 RP 轮次共用同一把全局 gate，避免轮次号/时间线并发写
        var gate = _gates.GetOrAdd(RoleplayGateKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var message = new ChatMessage(ChatRole.Assistant, text)
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [ChatMessageMarkers.Spoken] = true,
                },
            };
            TurnAttachments.AttachToLastAssistantText([message], attachments);

            await _rounds.AppendAsync(laneId, [], [message], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var row = _messages is null
                ? null
                : await _messages.GetLastAssistantAsync(laneId: laneId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            if (row is not null)
            {
                await _events.PublishAsync(
                        RpcEvents.MessageAppended,
                        new MessageAppendedEvent(
                            new MessageView(
                                row.Id,
                                row.RoundNo,
                                MessageRole.Assistant,
                                row.Kind,
                                MessageDisplay.Clean(row.Content),
                                row.ToolName,
                                row.CreatedAt,
                                row.Attachments,
                                row.Thinking,
                                row.ToolCallId,
                                row.ToolArguments,
                                row.ToolResult),
                            laneId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new TurnOutcome(
                "spk_" + Guid.NewGuid().ToString("N"),
                Succeeded: true,
                text,
                row?.Id,
                attachments);
        }
        finally
        {
            gate.Release();
        }
    }


    /// <summary>
    /// 验收并决定「现在跑」还是「合并进待跑槽」。
    ///
    /// 同一会话（工作 = sessionId，RP = Lane）跑着的时候，后来的消息**合并成同一轮**，
    /// 并给在跑的那一轮挂软打断（步边界停）；不排成一串各自回一条。
    /// 停止（<c>/stop</c>）会连待跑槽一起丢掉，见 <see cref="CancelAsync"/>。
    /// </summary>
    private async Task<(ActiveTurn Turn, bool ShouldRun)> StartAsync(
        TurnSendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new RpcException(RpcErrors.InvalidParams, "text 不能为空");
        }

        var target = await ResolveTargetAsync(request, cancellationToken).ConfigureAwait(false);
        if (_approvals.HasPending(target.SessionKey))
        {
            throw new RpcException(RpcErrors.ApprovalPending, "该会话还有待审批的工具调用，先处理审批再发消息");
        }

        lock (_queueGate)
        {
            var busy = _activeByKey.ContainsKey(target.SessionKey)
                       || _pendingByKey.ContainsKey(target.SessionKey);

            if (busy)
            {
                if (!_pendingByKey.TryGetValue(target.SessionKey, out var pending))
                {
                    pending = new ActiveTurn(_newId(), target, request);
                    _pendingByKey[target.SessionKey] = pending;
                    _turns.TryAdd(pending.TurnId, pending);
                }
                else
                {
                    pending.Append(request);
                }

                // 新消息 = 「等我这一步跑完就说这个」：步边界软打断，不掐思考/流式正文/工具执行。
                if (_activeByKey.TryGetValue(target.SessionKey, out var running))
                {
                    running.RequestSoftInterrupt();
                }

                return (pending, false);
            }

            var turn = new ActiveTurn(_newId(), target, request);
            _activeByKey[target.SessionKey] = turn;
            if (!_turns.TryAdd(turn.TurnId, turn))
            {
                throw new RpcException(RpcErrors.Busy, "轮次 id 冲突，请重试");
            }

            return (turn, true);
        }
    }

    /// <inheritdoc />
    public async Task<TurnRef> RetryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new RpcException(RpcErrors.InvalidParams, "sessionId 不能为空");
        }

        var session = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            // RP 传的是 Lane id：明确这是「不支持」，不是「id 写错了」
            var lane = await _lanes.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var isRoleplay = lane?.Mode == Mode.Roleplay
                             || string.Equals(sessionId, LaneIds.DesktopRoleplay, StringComparison.Ordinal);
            throw isRoleplay
                ? new RpcException(RpcErrors.InvalidParams, "RP 时间线不支持重试", new { sessionId })
                : new RpcException(RpcErrors.NotFound, "会话不存在", new { sessionId });
        }

        if (session.Mode != Mode.Work)
        {
            throw new RpcException(RpcErrors.InvalidParams, "RP 时间线不支持重试", new { sessionId });
        }

        if (session.Archived)
        {
            throw new RpcException(RpcErrors.SessionArchived, "会话已归档，不能重试", new { sessionId });
        }

        if (_currentTurnByKey.ContainsKey(sessionId))
        {
            // 重试要先截断历史；还有轮次在跑时截断会和它抢同一个 session
            throw new RpcException(RpcErrors.Busy, "该会话还有轮次在跑，等它结束再重试", new { sessionId });
        }

        if (!_lastTurns.TryGetValue(sessionId, out var last))
        {
            throw new RpcException(
                RpcErrors.InvalidParams,
                "没有可重试的轮次（daemon 重启后不支持重试，请手动再发一次）",
                new { sessionId });
        }

        if (last.Succeeded)
        {
            throw new RpcException(RpcErrors.InvalidParams, "上一轮已完成，不需要重试", new { sessionId });
        }

        if (last.Request.SystemInput || string.IsNullOrWhiteSpace(last.Request.Text))
        {
            throw new RpcException(RpcErrors.InvalidParams, "系统注入的轮次不能重试", new { sessionId });
        }

        // 失败可能发生在写入历史之前（排在队列里被取消）：那时没有可退回的消息，直接再发
        await _work
            .RewindLastInputAsync(sessionId, last.LaneId, last.Request.Text, cancellationToken)
            .ConfigureAwait(false);

        return await SendAsync(last.Request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAsync(string sessionId, string? turnId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        ActiveTurn? active = null;
        ActiveTurn? pending = null;

        lock (_queueGate)
        {
            if (!string.IsNullOrWhiteSpace(turnId))
            {
                if (_activeByKey.TryGetValue(sessionId, out var running) && running.TurnId == turnId)
                {
                    active = running;
                }
                else if (_pendingByKey.TryGetValue(sessionId, out var queued) && queued.TurnId == turnId)
                {
                    pending = queued;
                    _pendingByKey.Remove(sessionId);
                }
                else if (_turns.TryGetValue(turnId, out var direct))
                {
                    active = direct;
                }
            }
            else
            {
                // 停就停：不但在跑的那一轮要停，合并好还没开跑的那一轮也丢掉（不然停完又接着跑）。
                _activeByKey.TryGetValue(sessionId, out active);
                if (_pendingByKey.Remove(sessionId, out var queued))
                {
                    pending = queued;
                }
            }
        }

        if (pending is not null)
        {
            _turns.TryRemove(pending.TurnId, out _);
            _logger.LogInformation("丢弃待跑轮次：{TurnId}（{SessionKey}）", pending.TurnId, pending.SessionKey);
            await PublishAsync(
                    pending,
                    RpcEvents.TurnFailed,
                    new TurnFailedEvent(
                        pending.TurnId,
                        pending.EventSessionId,
                        TurnFailureCodes.Cancelled,
                        "已停止",
                        pending.LaneId,
                        pending.ReplyTarget))
                .ConfigureAwait(false);
            pending.Complete(succeeded: false, null);
            pending.Dispose();
        }

        if (active is null)
        {
            // 没有在跑的轮次：停就停是幂等的，不当错误
            return;
        }

        _logger.LogInformation("轮次取消：{TurnId}（{SessionKey}）", active.TurnId, active.SessionKey);
        await active.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RespondApprovalAsync(ApprovalRespondRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Decision == ApprovalDecision.Expired)
        {
            throw new RpcException(RpcErrors.InvalidParams, "expired 是系统结论，不能由客户端提交");
        }

        var resolved = await _approvals
            .ResolveAsync(request.ApprovalId, request.Decision, "user", request.Note, cancellationToken)
            .ConfigureAwait(false);
        if (!resolved)
        {
            throw new RpcException(
                RpcErrors.NotFound,
                "审批不存在或已结束",
                new { approvalId = request.ApprovalId });
        }
    }

    /// <inheritdoc />
    public int ActiveTurnCount => _turns.Count;

    /// <inheritdoc />
    public async Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_turns.IsEmpty)
        {
            return true;
        }

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadline = Task.Delay(timeout, deadlineCts.Token);
        while (!_turns.IsEmpty)
        {
            // 先取信号再复查：避免「复查之后、取信号之前」刚好收束导致等满超时
            var signal = _idleSignal;
            if (_turns.IsEmpty)
            {
                break;
            }

            var finished = await Task.WhenAny(signal.Task, deadline).ConfigureAwait(false);
            if (finished == deadline)
            {
                return _turns.IsEmpty;
            }
        }

        await deadlineCts.CancelAsync().ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public void CancelAll()
    {
        List<ActiveTurn> pending;
        List<ActiveTurn> active;
        lock (_queueGate)
        {
            pending = _pendingByKey.Values.ToList();
            _pendingByKey.Clear();
            active = _activeByKey.Values.ToList();
        }

        foreach (var turn in pending)
        {
            _turns.TryRemove(turn.TurnId, out _);
            _ = PublishAsync(
                turn,
                RpcEvents.TurnFailed,
                new TurnFailedEvent(
                    turn.TurnId,
                    turn.EventSessionId,
                    TurnFailureCodes.Cancelled,
                    "已停止",
                    turn.LaneId,
                    turn.ReplyTarget));
            turn.Complete(succeeded: false, null);
            turn.Dispose();
        }

        foreach (var turn in active)
        {
            _ = turn.CancelAsync();
        }
    }

    private async Task RunAsync(ActiveTurn turn)
    {
        // RP 只有一条跨 Lane 的时间线，所以整轮串行（不是按 Lane 各排各的）；
        // 工作模式仍按会话串行。
        var gate = _gates.GetOrAdd(
            turn.Mode == Mode.Roleplay ? RoleplayGateKey : turn.SessionKey,
            static _ => new SemaphoreSlim(1, 1));
        var acquired = false;
        var succeeded = false;
        try
        {
            await gate.WaitAsync(turn.Token).ConfigureAwait(false);
            acquired = true;
            _currentTurnByKey[turn.SessionKey] = turn.TurnId;

            await PublishAsync(turn, RpcEvents.TurnStarted, new TurnStartedEvent(
                    turn.TurnId, turn.EventSessionId, turn.LaneId, turn.ReplyTarget))
                .ConfigureAwait(false);

            // 附件先看一遍再喂模型：图片给描述、语音给转写（失败只降级，路径仍在正文里）
            var modelText = await ComposeModelTextAsync(turn).ConfigureAwait(false);

            UsageSummary usage;
            IReadOnlyList<UsageCall>? calls = null;
            UsageCall? lastCall = null;
            string? assistantText = null;
            long? messageId;
            IReadOnlyList<MessageAttachment>? attachments = null;
            if (turn.Mode == Mode.Roleplay)
            {
                var result = await _roleplay
                    .RunStreamingAsync(
                        turn.LaneId,
                        modelText,
                        _clock.GetLocalNow(),
                        new EventObserver(this, turn),
                        approvalHandler: (requests, token) => _approvals.RequestAsync(
                            turn.SessionKey, turn.TurnId, turn.LaneId, requests, token),
                        summarize: CreateSummarizer(),
                        cancellationToken: turn.Token,
                        systemInput: turn.SystemInput,
                        attachments: turn.Attachments,
                        displayText: turn.DisplayText,
                        softInterrupt: turn.ConsumeSoftInterrupt)
                    .ConfigureAwait(false);
                usage = result?.Usage ?? EmptyUsage;
                calls = result?.Calls;
                messageId = result?.MessageId;
                attachments = result?.Attachments;
            }
            else
            {
                var input = new ChatMessage(ChatRole.User, modelText);
                input = InputAttachments.Tag(input, turn.DisplayText, turn.Attachments);
                if (turn.SystemInput)
                {
                    // 纯系统注入（心跳/回执）：进上下文但不落可见消息
                    input.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                    input.AdditionalProperties[ChatMessageMarkers.SystemInput] = true;
                }

                var result = await _work
                    .RunStreamingAsync(
                        turn.SessionKey,
                        turn.LaneId,
                        input,
                        new EventObserver(this, turn),
                        options: null,
                        approvalHandler: (requests, token) => _approvals.RequestAsync(
                            turn.SessionKey, turn.TurnId, turn.LaneId, requests, token),
                        cancellationToken: turn.Token,
                        softInterrupt: turn.ConsumeSoftInterrupt)
                    .ConfigureAwait(false);
                usage = result.Usage;
                calls = result.Calls;
                messageId = result.MessageId;
                attachments = result.Attachments;
                assistantText = result.Text;
            }

            lastCall = calls is { Count: > 0 } ? calls[^1] : null;
            var context = lastCall is null
                ? null
                : new ContextUsage(
                    lastCall.InputTokens,
                    lastCall.OutputTokens,
                    lastCall.CacheHitTokens,
                    _clock.GetUtcNow());

            await RecordUsageAsync(turn, usage, calls).ConfigureAwait(false);
            await PublishAsync(
                    turn,
                    RpcEvents.TurnCompleted,
                    new TurnCompletedEvent(
                        turn.TurnId,
                        turn.EventSessionId,
                        messageId,
                        usage,
                        turn.LaneId,
                        turn.ReplyTarget,
                        attachments,
                        context))
                .ConfigureAwait(false);
            turn.Complete(succeeded: true, messageId, attachments);
            succeeded = true;

            // 首轮之后用标题小模型给会话起名；不阻塞轮次结束，失败只记日志。
            if (turn.Mode == Mode.Work && turn.SessionId is { Length: > 0 } sessionId)
            {
                _ = TryAutoTitleAsync(sessionId, turn.LaneId, turn.DisplayText, assistantText);
            }
        }
        catch (OperationCanceledException)
        {
            await PublishAsync(
                    turn,
                    RpcEvents.TurnFailed,
                    new TurnFailedEvent(
                        turn.TurnId, turn.EventSessionId, TurnFailureCodes.Cancelled, "已停止", turn.LaneId, turn.ReplyTarget))
                .ConfigureAwait(false);
            turn.Complete(succeeded: false, null);
        }
        catch (RpcException ex)
        {
            await PublishAsync(
                    turn,
                    RpcEvents.TurnFailed,
                    new TurnFailedEvent(
                        turn.TurnId, turn.EventSessionId, RpcErrors.NameOf(ex.Code), ex.Message, turn.LaneId, turn.ReplyTarget))
                .ConfigureAwait(false);
            turn.Complete(succeeded: false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "轮次失败：{TurnId}（{SessionKey}）", turn.TurnId, turn.SessionKey);
            await PublishAsync(
                    turn,
                    RpcEvents.TurnFailed,
                    new TurnFailedEvent(
                        turn.TurnId, turn.EventSessionId, TurnFailureCodes.Internal, ex.Message, turn.LaneId, turn.ReplyTarget))
                .ConfigureAwait(false);
            turn.Complete(succeeded: false, null);
        }
        finally
        {
            // 兜底：任何路径都要唤醒等待者（TrySetResult 幂等，成功那次的 true 不会被覆盖）
            turn.Complete(succeeded: false, null);
            RememberLastTurn(turn, succeeded);

            _turns.TryRemove(turn.TurnId, out _);
            if (_currentTurnByKey.TryGetValue(turn.SessionKey, out var current) && current == turn.TurnId)
            {
                _currentTurnByKey.TryRemove(turn.SessionKey, out _);
            }

            if (acquired)
            {
                gate.Release();
            }

            turn.Dispose();

            // 奔完这一轮再看待跑槽：有就把它升成在跑的一轮（忙时合并过的新消息只跑一次）。
            ActiveTurn? next = null;
            lock (_queueGate)
            {
                if (_activeByKey.TryGetValue(turn.SessionKey, out var running)
                    && ReferenceEquals(running, turn))
                {
                    _activeByKey.Remove(turn.SessionKey);
                    if (_pendingByKey.Remove(turn.SessionKey, out var pending))
                    {
                        _activeByKey[turn.SessionKey] = pending;
                        next = pending;
                    }
                }
            }

            if (_turns.IsEmpty)
            {
                var signal = _idleSignal;
                _idleSignal = NewIdleSignal();
                signal.TrySetResult();
            }

            if (next is not null)
            {
                _ = RunAsync(next);
            }
        }
    }

    /// <summary>组装给模型看的正文：合并后的正文 + 附件路径 + 自动分析结果。</summary>
    private async Task<string> ComposeModelTextAsync(ActiveTurn turn)
    {
        var attachments = turn.Attachments;
        if (attachments is not { Count: > 0 } || _analyzer is null)
        {
            return InputAttachments.ComposeModelText(turn.ModelText, attachments);
        }

        IReadOnlyList<AttachmentAnalysis> analyses = [];
        try
        {
            analyses = await _analyzer.AnalyzeAsync(attachments, turn.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (turn.Token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 自动分析是锦上添花：失败不拦对话，模型仍能按路径自己调工具
            _logger.LogWarning(ex, "附件自动分析失败：{TurnId}", turn.TurnId);
        }

        return InputAttachments.ComposeModelText(turn.ModelText, attachments, analyses);
    }

    private async Task RecordUsageAsync(
        ActiveTurn turn,
        UsageSummary usage,
        IReadOnlyList<UsageCall>? calls = null)
    {
        if (_usage is null || usage.Calls == 0)
        {
            return;
        }

        try
        {
            var model = _config?.Models.Main.ModelId ?? "unknown";

            // 一次模型调用一行：最后一次调用的输入就是当前提示词大小，桌面据此显示真实上下文占用。
            // 端点不回 usage 时 calls 为空，退回整轮聚合（老行为）。
            if (calls is { Count: > 0 })
            {
                foreach (var call in calls)
                {
                    await _usage.AddAsync(
                            turn.EventSessionId,
                            turn.TurnId,
                            "chat.call",
                            model,
                            call.InputTokens,
                            call.OutputTokens,
                            call.CacheHitTokens,
                            Math.Max(0, call.InputTokens - call.CacheHitTokens),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                await _usage.AddAsync(
                        turn.EventSessionId,
                        turn.TurnId,
                        "chat",
                        model,
                        usage.InputTokens,
                        usage.OutputTokens,
                        usage.CacheHitTokens,
                        usage.CacheMissTokens,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            // 每轮一条聚合：桌面据此刷新缓存命中率，不用等 status.get
            await _events.PublishAsync(
                    RpcEvents.UsageUpdated,
                    new UsageDto(
                        model,
                        usage.InputTokens,
                        usage.OutputTokens,
                        usage.CacheHitTokens,
                        usage.CacheMissTokens,
                        _clock.GetUtcNow()),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "用量记录失败：{TurnId}", turn.TurnId);
        }
    }

    /// <summary>
    /// 首轮之后用标题小模型给工作会话起名。会话已经有名字、或小模型没配置时直接跳过；
    /// 起名失败移除标记，下一轮再试。不阻塞轮次结束。
    /// </summary>
    private async Task TryAutoTitleAsync(
        string sessionId,
        string? laneId,
        string? userText,
        string? assistantText)
    {
        if (string.IsNullOrWhiteSpace(userText) || string.IsNullOrWhiteSpace(assistantText))
        {
            return;
        }

        if (_smallClient.Value is not { } client || !_titleAttempts.TryAdd(sessionId, 0))
        {
            return;
        }

        try
        {
            var session = await _sessions.GetAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
            if (session is null || session.Mode != Mode.Work || !IsUntitled(session.Title))
            {
                return;
            }

            var prompt =
                "给下面这段对话起一个简短标题：不超过 12 个汉字，只输出标题本身，"
                + "不要引号、句号、冒号或「标题：」前缀。\n\n"
                + $"用户：{Clip(userText, 400)}\n\n"
                + $"助手：{Clip(assistantText, 700)}";

            var response = await client
                .GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            await RecordTitleUsageAsync(sessionId, response.Usage).ConfigureAwait(false);

            var title = NormalizeTitle(response.Text);
            if (title.Length == 0)
            {
                return;
            }

            await _sessions.RenameAsync(sessionId, title, CancellationToken.None).ConfigureAwait(false);
            await _events.PublishAsync(
                    RpcEvents.SessionRenamed,
                    new SessionRenamedEvent(sessionId, title, laneId),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 起名是锦上添花：失败不拦对话，下一轮还可能再试
            _titleAttempts.TryRemove(sessionId, out _);
            _logger.LogWarning(ex, "自动起名失败：{SessionId}", sessionId);
        }
    }

    private async Task RecordTitleUsageAsync(string sessionId, UsageDetails? usage)
    {
        if (_usage is null || usage is null)
        {
            return;
        }

        var input = usage.InputTokenCount ?? 0;
        var output = usage.OutputTokenCount ?? 0;
        var cached = usage.CachedInputTokenCount ?? 0;
        if (input == 0 && output == 0 && cached == 0)
        {
            return;
        }

        try
        {
            await _usage.AddAsync(
                    sessionId,
                    null,
                    "title",
                    _config?.Models.Small.ModelId ?? "unknown",
                    (int)input,
                    (int)output,
                    (int)cached,
                    Math.Max(0, (int)(input - cached)),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "标题用量记录失败：{SessionId}", sessionId);
        }
    }

    private static bool IsUntitled(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var trimmed = title.Trim();
        return trimmed is "新的工作会话" or "未命名会话"
            || (trimmed.StartsWith("会话 ", StringComparison.Ordinal) && trimmed.Length <= 24);
    }

    private static string NormalizeTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var line = text.Replace("\r", "")
            .Split('\n')
            .Select(static value => value.Trim())
            .FirstOrDefault(static value => value.Length > 0);
        if (line is null)
        {
            return string.Empty;
        }

        line = line.TrimStart('#', '-', '*', ' ');
        foreach (var prefix in (string[])["标题：", "标题:", "Title:", "title:"])
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                line = line[prefix.Length..].Trim();
            }
        }

        line = line.Trim().Trim('"', '\'', '“', '”', '‘', '’', '「', '」', '《', '》', '。', '，', ',', '.', '!', '！');
        return Clip(line, 18);
    }

    private static string Clip(string text, int max)
        => text.Length <= max ? text : text[..max];

    private async Task<TurnTarget> ResolveTargetAsync(TurnSendRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var session = await _sessions.GetAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                throw new RpcException(
                    RpcErrors.NotFound, "会话不存在", new { sessionId = request.SessionId });
            }

            if (session.Archived)
            {
                throw new RpcException(
                    RpcErrors.SessionArchived, "会话已归档，不能继续发消息", new { sessionId = session.Id });
            }

            var laneId = session.LaneId ?? request.LaneId ?? session.SessionKey;
            return new TurnTarget(session.Id, session.Id, laneId, session.Mode);
        }

        if (string.IsNullOrWhiteSpace(request.LaneId))
        {
            throw new RpcException(RpcErrors.InvalidParams, "sessionId 与 laneId 至少给一个");
        }

        var lane = await _lanes.GetAsync(request.LaneId, cancellationToken).ConfigureAwait(false);
        var mode = lane?.Mode
            ?? (string.Equals(request.LaneId, LaneIds.DesktopRoleplay, StringComparison.Ordinal)
                ? Mode.Roleplay
                : (Mode?)null);

        if (mode is null)
        {
            throw new RpcException(RpcErrors.NotFound, "Lane 不存在", new { laneId = request.LaneId });
        }

        if (mode != Mode.Roleplay)
        {
            throw new RpcException(
                RpcErrors.InvalidParams, "工作模式必须给 sessionId（会话由会话层创建）", new { laneId = request.LaneId });
        }

        // RP 没有会话：Lane 就是连续流，事件里的 sessionId 用 Lane id 占位
        return new TurnTarget(request.LaneId, null, request.LaneId, Mode.Roleplay);
    }

    private Func<string, CancellationToken, Task<string>>? CreateSummarizer()
    {
        if (_smallClient.Value is not { } client)
        {
            return null;
        }

        return async (prompt, cancellationToken) =>
        {
            var response = await client
                .GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Text ?? string.Empty;
        };
    }

    private IChatClient? CreateSmallClient()
    {
        if (_models is null)
        {
            return null;
        }

        try
        {
            return _models.CreateSmall();
        }
        catch (Exception ex)
        {
            // 小模型缺 key 只降级为「本轮不摘要」，不能拦对话
            _logger.LogWarning(ex, "小模型不可用，RP 摘要本轮跳过");
            return null;
        }
    }

    private async Task PublishAsync(ActiveTurn turn, string eventName, object payload)
    {
        try
        {
            await _events.PublishAsync(eventName, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "事件推送失败：{Event}（{TurnId}）", eventName, turn.TurnId);
        }
    }

    /// <summary>只记工作模式：RP 的时间线不删，重试不是它的语义。</summary>
    private void RememberLastTurn(ActiveTurn turn, bool succeeded)
    {
        if (turn.Mode == Mode.Work)
        {
            _lastTurns[turn.SessionKey] = new LastTurn(turn.ToRequest(), turn.LaneId, succeeded);
        }
    }

    private static readonly UsageSummary EmptyUsage = new(0, 0, 0, 0, 0);

    private static TaskCompletionSource NewIdleSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record TurnTarget(string SessionKey, string? SessionId, string LaneId, Mode Mode);

    /// <summary>上一轮的可重试信息（请求原文 + 是否成功）。</summary>
    private sealed record LastTurn(TurnSendRequest Request, string LaneId, bool Succeeded);

    private sealed class ActiveTurn : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly System.Text.StringBuilder _text = new();
        private readonly TaskCompletionSource<TurnOutcome> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<TurnSendRequest> _inputs = [];
        private volatile bool _softInterrupt;

        public ActiveTurn(string turnId, TurnTarget target, TurnSendRequest request)
        {
            TurnId = turnId;
            SessionKey = target.SessionKey;
            SessionId = target.SessionId;
            LaneId = target.LaneId;
            Mode = target.Mode;
            _inputs.Add(request);
            Rebuild();
        }

        public string TurnId { get; }

        /// <summary>排队/串行键：工作模式 = sessionId，RP = Lane id（RP 的串行另用全局 gate）。</summary>
        public string SessionKey { get; }

        public string? SessionId { get; }

        public string LaneId { get; }

        public Mode Mode { get; }

        /// <summary>给人看的正文：忙时合并只取用户说的话（系统注入不露出来）。</summary>
        public string DisplayText { get; private set; } = string.Empty;

        /// <summary>给模型的正文：用户消息与系统注入按到达顺序合并。</summary>
        public string ModelText { get; private set; } = string.Empty;

        /// <summary>本轮是否全是系统注入（没有用户说的话）。</summary>
        public bool SystemInput { get; private set; }

        public IReadOnlyList<MessageAttachment>? Attachments { get; private set; }

        public string? ReplyTarget { get; private set; }

        /// <summary>事件里的会话字段：工作模式用 sessionId，RP 用 Lane id（RP 没有会话）。</summary>
        public string EventSessionId => SessionId ?? LaneId;

        public CancellationToken Token => _cts.Token;

        /// <summary>等本轮跑完（主动调用方用）。</summary>
        public Task<TurnOutcome> Completion => _completion.Task;

        /// <summary>忙时把新消息并进这一轮（下一轮只跑一次）。</summary>
        public void Append(TurnSendRequest request)
        {
            _inputs.Add(request);
            Rebuild();
        }

        /// <summary>重跑用：把合并后的可见正文与附件还原成一个请求。</summary>
        public TurnSendRequest ToRequest() => new(
            DisplayText,
            SessionId: SessionId,
            LaneId: LaneId,
            Attachments: Attachments,
            ReplyTarget: ReplyTarget,
            SystemInput: SystemInput);

        /// <summary>给在跑的这一轮挂软打断标记（步边界才生效）。</summary>
        public void RequestSoftInterrupt() => _softInterrupt = true;

        /// <summary>消费一次软打断标记（consume-once）。</summary>
        public bool ConsumeSoftInterrupt()
        {
            if (!_softInterrupt)
            {
                return false;
            }

            _softInterrupt = false;
            return true;
        }

        /// <summary>累计流式正文（与桌面看到的一致）。</summary>
        public void AppendDelta(string delta) => _text.Append(delta);

        /// <summary>收束一次；重复调用只有第一次生效。</summary>
        public void Complete(bool succeeded, long? messageId, IReadOnlyList<MessageAttachment>? attachments = null)
            => _completion.TrySetResult(new TurnOutcome(
                TurnId,
                succeeded,
                _text.ToString().Trim(),
                messageId,
                attachments));

        public Task CancelAsync()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已经收束的轮次，取消是幂等的
            }

            return Task.CompletedTask;
        }

        public void Dispose() => _cts.Dispose();

        /// <summary>按合并进来的输入重算「给人看 / 给模型看」的正文。</summary>
        private void Rebuild()
        {
            var user = new List<string>();
            var all = new List<string>();
            var attachments = new List<MessageAttachment>();

            foreach (var input in _inputs)
            {
                if (string.IsNullOrWhiteSpace(input.Text))
                {
                    continue;
                }

                all.Add(input.Text);
                if (!input.SystemInput)
                {
                    user.Add(input.Text);
                }

                if (input.Attachments is { Count: > 0 })
                {
                    attachments.AddRange(input.Attachments);
                }

                if (!string.IsNullOrEmpty(input.ReplyTarget))
                {
                    ReplyTarget = input.ReplyTarget;
                }
            }

            ModelText = string.Join("\n\n", all);
            SystemInput = user.Count == 0;
            DisplayText = user.Count > 0 ? string.Join("\n\n", user) : ModelText;
            Attachments = attachments.Count > 0 ? attachments : null;
        }
    }

    private sealed class EventObserver : TurnObserver
    {
        private readonly TurnService _owner;
        private readonly ActiveTurn _turn;

        public EventObserver(TurnService owner, ActiveTurn turn)
        {
            _owner = owner;
            _turn = turn;
        }

        public override ValueTask OnDeltaAsync(string delta, CancellationToken cancellationToken = default)
        {
            _turn.AppendDelta(delta);
            return new(_owner.PublishAsync(
                _turn,
                RpcEvents.TurnDelta,
                new TurnDeltaEvent(_turn.TurnId, _turn.EventSessionId, delta, _turn.LaneId)));
        }

        public override ValueTask OnThinkingAsync(string delta, CancellationToken cancellationToken = default)
            => new(_owner.PublishAsync(
                _turn,
                RpcEvents.TurnThinking,
                new TurnThinkingEvent(_turn.TurnId, _turn.EventSessionId, delta, _turn.LaneId)));

        public override ValueTask OnToolAsync(
            string phase,
            string toolName,
            string? summary,
            string? callId = null,
            string? arguments = null,
            string? result = null,
            CancellationToken cancellationToken = default)
            => new(_owner.PublishAsync(
                _turn,
                RpcEvents.TurnTool,
                new TurnToolEvent(
                    _turn.TurnId,
                    _turn.EventSessionId,
                    phase,
                    toolName,
                    summary,
                    _turn.LaneId,
                    callId,
                    arguments,
                    result)));
    }
}
