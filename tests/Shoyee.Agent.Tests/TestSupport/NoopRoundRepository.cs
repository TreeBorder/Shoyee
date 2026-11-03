using Microsoft.Extensions.AI;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>RP 记录器只读的轮次仓库空实现。</summary>
internal sealed class NoopRoundRepository : IRoundRepository
{
    public Task<RoundRecord> AppendAsync(
        string laneId,
        IReadOnlyList<ChatMessage> request,
        IReadOnlyList<ChatMessage> response,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<RoundRecord>> GetRecentAsync(
        int count = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RoundRecord>>([]);

    public Task<IReadOnlyList<RoundRecord>> GetAfterAsync(
        int afterRoundNo,
        int count = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RoundRecord>>([]);

    public Task<int> GetMaxRoundNoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
