using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Approval;

/// <summary>
/// 工具审批台（HITL）：暂存框架浮出的 <see cref="ToolApprovalRequestContent"/>，
/// 等 <c>approval.respond</c> 落决定，再把与之配对的 <see cref="ToolApprovalResponseContent"/>
/// 交回仍挂起的那一轮继续跑。
///
/// 一次 <see cref="RequestAsync"/> 收到的整批请求共用一个决定（与 Secretary 语义一致：
/// 一回合一屏审批、批量批准/拒绝），但每个调用各写一行 <c>approvals</c> 记录。
///
/// 超时（默认 10 分钟）按拒绝处理并落 <c>expired</c>；轮次被取消时同样补拒绝，避免历史里留下悬空调用。
/// 待审批状态只在内存：进程重启后她会重新问（与 Secretary 相同）。
/// </summary>
public sealed class ApprovalBroker : IApprovalConsole, IDisposable
{
    /// <summary>默认审批超时。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private readonly ITurnEventSink _events;
    private readonly IApprovalRepository? _repository;
    private readonly ILogger<ApprovalBroker> _logger;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    private readonly Dictionary<string, PendingBatch> _batches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _approvalToBatch = new(StringComparer.Ordinal);

    private bool _disposed;

    /// <summary>创建审批台。</summary>
    /// <param name="events">事件出口。</param>
    /// <param name="repository">审批记录仓库（可空：测试/无存储场景只走内存）。</param>
    /// <param name="timeout">超时时长；null = <see cref="DefaultTimeout"/>。</param>
    /// <param name="clock">时间源（测试可注入）。</param>
    /// <param name="logger">日志。</param>
    public ApprovalBroker(
        ITurnEventSink events,
        IApprovalRepository? repository = null,
        TimeSpan? timeout = null,
        TimeProvider? clock = null,
        ILogger<ApprovalBroker>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        _events = events;
        _repository = repository;
        _timeout = timeout ?? DefaultTimeout;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ApprovalBroker>.Instance;
    }

    /// <summary>当前待审批批次数（测试/状态用）。</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _batches.Count;
            }
        }
    }

    /// <summary>该会话是否有待审批请求。</summary>
    /// <param name="sessionId">会话 id（RP 用 Lane id）。</param>
    public bool HasPending(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_gate)
        {
            return _batches.Values.Any(b => string.Equals(b.SessionId, sessionId, StringComparison.Ordinal));
        }
    }

    /// <summary>该会话当前的待审批请求（无则空）。</summary>
    /// <param name="sessionId">会话 id（RP 用 Lane id）。</param>
    public IReadOnlyList<ApprovalRequestDto> Peek(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_gate)
        {
            return _batches.Values
                .Where(b => string.Equals(b.SessionId, sessionId, StringComparison.Ordinal))
                .SelectMany(static b => b.Dtos)
                .ToList();
        }
    }

    /// <summary>
    /// 挂起一批待审批调用：登记记录、推 <c>approval.request</c>、等决定。
    /// 超时/轮次取消会自动按拒绝收束（调用方无需自行清理）。
    /// </summary>
    /// <param name="sessionId">会话 id（RP 用 Lane id）。</param>
    /// <param name="turnId">轮次 id。</param>
    /// <param name="laneId">Lane id（可见性过滤用）。</param>
    /// <param name="requests">框架浮出的待审批调用。</param>
    /// <param name="cancellationToken">取消令牌（轮次被打断时触发）。</param>
    /// <returns>与 <paramref name="requests"/> 一一配对的审批响应内容，交回 agent 续跑。</returns>
    public async Task<IReadOnlyList<AIContent>> RequestAsync(
        string sessionId,
        string turnId,
        string? laneId,
        IReadOnlyList<ToolApprovalRequestContent> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            return [];
        }

        var now = _clock.GetUtcNow();
        var expiresAt = _timeout == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : now + _timeout;
        var dtos = requests.Select(_ => NewRequestDto(sessionId, turnId, laneId, _, now, expiresAt)).ToList();
        var batch = new PendingBatch(sessionId, turnId, laneId, requests, dtos);
        var key = "apv_batch_" + Guid.NewGuid().ToString("N");

        lock (_gate)
        {
            _batches[key] = batch;
            foreach (var dto in dtos)
            {
                _approvalToBatch[dto.ApprovalId] = key;
            }
        }

        foreach (var record in dtos.Select(dto => ToRecord(dto)))
        {
            await PersistAsync(record, cancellationToken).ConfigureAwait(false);
        }

        foreach (var dto in dtos)
        {
            await PublishAsync(RpcEvents.ApprovalRequest, dto, cancellationToken).ConfigureAwait(false);
        }

        if (_timeout != Timeout.InfiniteTimeSpan)
        {
            _ = ExpireAfterAsync(batch);
        }

        try
        {
            return await batch.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 轮次被打断/超时由调用方取消：补一条拒绝，框架不会留下悬空调用
            await ResolveCoreAsync(batch, ApprovalDecision.Expired, "turn-cancelled", "轮次已取消", CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>落一份决定（按 <paramref name="approvalId"/> 找到它所属的批次，整批一起决定）。</summary>
    /// <param name="approvalId">审批 id（事件里给客户端的那个）。</param>
    /// <param name="decision">结论。</param>
    /// <param name="decidedBy">决定者（desktop / wechat / timeout）。</param>
    /// <param name="note">备注。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否找到并落了决定。</returns>
    public async Task<bool> ResolveAsync(
        string approvalId,
        ApprovalDecision decision,
        string decidedBy,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        PendingBatch? batch;
        lock (_gate)
        {
            batch = _approvalToBatch.TryGetValue(approvalId, out var key) && _batches.TryGetValue(key, out var found)
                ? found
                : null;
        }

        if (batch is null)
        {
            return false;
        }

        return await ResolveCoreAsync(batch, decision, decidedBy, note, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> ResolveAllAsync(
        string sessionOrLaneId,
        ApprovalDecision decision,
        string decidedBy,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionOrLaneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);
        if (decision == ApprovalDecision.Expired)
        {
            throw new ArgumentException("expired 是系统结论，不能作为人工决定", nameof(decision));
        }

        List<PendingBatch> batches;
        lock (_gate)
        {
            batches = _batches.Values
                .Where(b => string.Equals(b.SessionId, sessionOrLaneId, StringComparison.Ordinal))
                .ToList();
        }

        var resolved = 0;
        foreach (var batch in batches)
        {
            if (await ResolveCoreAsync(batch, decision, decidedBy, note, cancellationToken).ConfigureAwait(false))
            {
                resolved++;
            }
        }

        return resolved;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<PendingBatch> batches;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            batches = _batches.Values.ToList();
            _batches.Clear();
            _approvalToBatch.Clear();
        }

        foreach (var batch in batches)
        {
            batch.Timeout.Cancel();
            batch.Timeout.Dispose();
            batch.Completion.TrySetCanceled();
        }
    }

    private async Task<bool> ResolveCoreAsync(
        PendingBatch batch,
        ApprovalDecision decision,
        string decidedBy,
        string? note,
        CancellationToken cancellationToken)
    {
        List<ApprovalRequestDto> dtos;
        lock (_gate)
        {
            if (batch.Resolved)
            {
                return false;
            }

            batch.Resolved = true;
            dtos = batch.Dtos.ToList();
            foreach (var dto in dtos)
            {
                _approvalToBatch.Remove(dto.ApprovalId);
            }

            foreach (var key in _batches.Where(p => ReferenceEquals(p.Value, batch)).Select(static p => p.Key).ToList())
            {
                _batches.Remove(key);
            }
        }

        batch.Timeout.Cancel();
        var at = _clock.GetUtcNow();
        foreach (var dto in dtos)
        {
            await PersistResolveAsync(dto.ApprovalId, decision, decidedBy, at, cancellationToken)
                .ConfigureAwait(false);
            await PublishAsync(
                    RpcEvents.ApprovalResolved,
                    new ApprovalResolvedDto(dto.ApprovalId, decision, at, dto.SessionId, dto.LaneId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        batch.Completion.TrySetResult(BuildResponses(batch.Requests, decision, note));
        return true;
    }

    private async Task ExpireAfterAsync(PendingBatch batch)
    {
        try
        {
            await Task.Delay(_timeout, _clock, batch.Timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var resolved = await ResolveCoreAsync(
                batch, ApprovalDecision.Expired, "timeout", "审批超时，按拒绝处理", CancellationToken.None)
            .ConfigureAwait(false);
        if (resolved)
        {
            _logger.LogWarning("审批超时未答复，已按拒绝处理：{TurnId}", batch.TurnId);
        }
    }

    private static IReadOnlyList<AIContent> BuildResponses(
        IReadOnlyList<ToolApprovalRequestContent> requests,
        ApprovalDecision decision,
        string? note)
        => requests.Select(request => decision switch
        {
            ApprovalDecision.Approved => (AIContent)request.CreateResponse(true, note),
            ApprovalDecision.Always => request.CreateAlwaysApproveToolResponse(note),
            _ => request.CreateResponse(false, note ?? "已拒绝"),
        }).ToList();

    private static ApprovalRequestDto NewRequestDto(
        string sessionId,
        string turnId,
        string? laneId,
        ToolApprovalRequestContent request,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var (name, command, arguments) = Describe(request.ToolCall);
        return new ApprovalRequestDto(
            "apv_" + Guid.NewGuid().ToString("N"),
            sessionId,
            turnId,
            name,
            command,
            arguments,
            now,
            expiresAt,
            laneId);
    }

    private static ApprovalRecord ToRecord(ApprovalRequestDto dto)
        => new(
            dto.ApprovalId,
            dto.SessionId,
            dto.TurnId,
            dto.ToolName,
            dto.Arguments is { } arguments ? arguments.GetRawText() : "{}",
            Decision: null,
            DecidedBy: null,
            DecidedAt: null,
            dto.CreatedAt);

    private static (string Name, string? Command, JsonElement? Arguments) Describe(ToolCallContent toolCall)
    {
        if (toolCall is not FunctionCallContent call)
        {
            return (toolCall.CallId ?? "tool", null, null);
        }

        var arguments = call.Arguments is null || call.Arguments.Count == 0
            ? (JsonElement?)null
            : JsonSerializer.SerializeToElement(call.Arguments, JsonWire.Options);

        string? command = null;
        if (call.Arguments is not null
            && call.Arguments.TryGetValue("command", out var raw)
            && raw is not null)
        {
            command = raw switch
            {
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                string text => text,
                _ => raw.ToString(),
            };
        }

        return (call.Name, command, arguments);
    }

    private async Task PersistAsync(ApprovalRecord record, CancellationToken cancellationToken)
    {
        if (_repository is null)
        {
            return;
        }

        try
        {
            await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 台账写失败不能卡住审批本身体
            _logger.LogWarning(ex, "审批记录写入失败：{ApprovalId}", record.RequestId);
        }
    }

    private async Task PersistResolveAsync(
        string approvalId,
        ApprovalDecision decision,
        string decidedBy,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (_repository is null)
        {
            return;
        }

        try
        {
            await _repository.ResolveAsync(approvalId, decision, decidedBy, at, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "审批结论写入失败：{ApprovalId}", approvalId);
        }
    }

    private async Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await _events.PublishAsync(eventName, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "审批事件推送失败：{Event}", eventName);
        }
    }

    private sealed class PendingBatch
    {
        public PendingBatch(
            string sessionId,
            string turnId,
            string? laneId,
            IReadOnlyList<ToolApprovalRequestContent> requests,
            IReadOnlyList<ApprovalRequestDto> dtos)
        {
            SessionId = sessionId;
            TurnId = turnId;
            LaneId = laneId;
            Requests = requests;
            Dtos = dtos;
        }

        public string SessionId { get; }

        public string TurnId { get; }

        public string? LaneId { get; }

        public IReadOnlyList<ToolApprovalRequestContent> Requests { get; }

        public IReadOnlyList<ApprovalRequestDto> Dtos { get; }

        public TaskCompletionSource<IReadOnlyList<AIContent>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenSource Timeout { get; } = new();

        public bool Resolved { get; set; }
    }
}
