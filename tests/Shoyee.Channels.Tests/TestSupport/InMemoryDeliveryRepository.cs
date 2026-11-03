using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Channels.Tests.TestSupport;

/// <summary>内存投递台账：语义对齐 SQLite 实现（pending/failed 复用、attempts 递增、超限放弃）。</summary>
internal sealed class InMemoryDeliveryRepository : IDeliveryRepository
{
    private long _nextId = 1;

    public List<DeliveryRecord> Rows { get; } = [];

    public int Enqueued { get; private set; }

    public Task<DeliveryRecord> EnqueueAsync(
        string laneId,
        string? sessionId,
        string kind,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        var existing = Rows.FirstOrDefault(r =>
            r.LaneId == laneId
            && r.Kind == kind
            && r.PayloadJson == payloadJson
            && r.State is DeliveryState.Pending or DeliveryState.Failed);
        if (existing is not null)
        {
            return Task.FromResult(existing);
        }

        Enqueued++;
        var now = DateTimeOffset.UnixEpoch;
        var row = new DeliveryRecord(
            _nextId++,
            laneId,
            sessionId,
            kind,
            payloadJson,
            DeliveryState.Pending,
            Attempts: 0,
            NextAttemptAt: null,
            LastError: null,
            now,
            now);
        Rows.Add(row);
        return Task.FromResult(row);
    }

    public Task<IReadOnlyList<DeliveryRecord>> GetDueAsync(
        DateTimeOffset now,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DeliveryRecord> due = Rows
            .Where(r => r.State is DeliveryState.Pending or DeliveryState.Failed)
            .Where(r => r.NextAttemptAt is null || r.NextAttemptAt <= now)
            .Take(limit)
            .ToList();
        return Task.FromResult(due);
    }

    public Task<IReadOnlyList<DeliveryRecord>> GetPendingForLaneAsync(
        string laneId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DeliveryRecord> rows = Rows
            .Where(r => r.LaneId == laneId)
            .Where(r => r.State is DeliveryState.Pending or DeliveryState.Failed)
            .OrderBy(r => r.Id)
            .Take(limit)
            .ToList();
        return Task.FromResult(rows);
    }

    public Task MarkSentAsync(long id, CancellationToken cancellationToken = default)
    {
        Replace(id, r => r with { State = DeliveryState.Sent, LastError = null });
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        long id,
        string error,
        DateTimeOffset nextAttemptAt,
        int maxAttempts = 10,
        CancellationToken cancellationToken = default)
    {
        Replace(id, r => r with
        {
            Attempts = r.Attempts + 1,
            LastError = error,
            NextAttemptAt = nextAttemptAt,
            State = r.Attempts + 1 >= maxAttempts ? DeliveryState.Abandoned : DeliveryState.Failed,
        });
        return Task.CompletedTask;
    }

    private void Replace(long id, Func<DeliveryRecord, DeliveryRecord> update)
    {
        var index = Rows.FindIndex(r => r.Id == id);
        if (index >= 0)
        {
            Rows[index] = update(Rows[index]);
        }
    }
}
