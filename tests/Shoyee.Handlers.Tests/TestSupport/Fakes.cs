using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Handlers.Tests.TestSupport;

/// <summary>内存审批台：记录批量答复，没有待审批时返回 0。</summary>
internal sealed class FakeApprovalConsole : IApprovalConsole
{
    public List<ApprovalRequestDto> Pending { get; } = [];

    public List<(string Target, ApprovalDecision Decision, string DecidedBy)> Resolved { get; } = [];

    public IReadOnlyList<ApprovalRequestDto> Peek(string sessionOrLaneId) => Pending;

    public Task<int> ResolveAllAsync(
        string sessionOrLaneId,
        ApprovalDecision decision,
        string decidedBy,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (Pending.Count == 0)
        {
            return Task.FromResult(0);
        }

        var count = Pending.Count;
        Pending.Clear();
        Resolved.Add((sessionOrLaneId, decision, decidedBy));
        return Task.FromResult(count);
    }
}

/// <summary>只记录取消目标的轮次服务。</summary>
internal sealed class StubTurnService : ITurnService
{
    public List<(string SessionId, string? TurnId)> Cancelled { get; } = [];

    public Task<TurnRef> SendAsync(TurnSendRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnRef("trn_stub"));

    public Task<TurnOutcome> SendAndWaitAsync(
        TurnSendRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnOutcome("trn_stub", true, string.Empty));

    public Task<TurnOutcome> AppendSpokenAsync(
        string laneId,
        string text,
        IReadOnlyList<MessageAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnOutcome("trn_stub", true, text, null, attachments));

    public Task<TurnRef> RetryAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnRef("trn_stub"));

    public Task CancelAsync(string sessionId, string? turnId, CancellationToken cancellationToken = default)
    {
        Cancelled.Add((sessionId, turnId));
        return Task.CompletedTask;
    }

    public Task RespondApprovalAsync(
        ApprovalRespondRequest request,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
