using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Channels.Tests.TestSupport;

/// <summary>记录轮次请求的假 TurnService。</summary>
internal sealed class StubTurnService : ITurnService
{
    public List<TurnSendRequest> Requests { get; } = [];

    public Exception? Failure { get; init; }

    public Task<TurnRef> SendAsync(TurnSendRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Failure is null
            ? Task.FromResult(new TurnRef("trn_" + Requests.Count))
            : Task.FromException<TurnRef>(Failure);
    }

    public Task<TurnRef> RetryAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnRef("trn_retry"));

    public Task CancelAsync(string sessionId, string? turnId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<TurnOutcome> SendAndWaitAsync(TurnSendRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Failure is null
            ? Task.FromResult(new TurnOutcome("trn_" + Requests.Count, true, "好了", null))
            : Task.FromException<TurnOutcome>(Failure);
    }

    public Task<TurnOutcome> AppendSpokenAsync(
        string laneId,
        string text,
        IReadOnlyList<MessageAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnOutcome("spk_" + Requests.Count, true, text, null, attachments));

    public Task RespondApprovalAsync(ApprovalRespondRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
