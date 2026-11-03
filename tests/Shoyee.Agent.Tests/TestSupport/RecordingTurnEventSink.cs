using Shoyee.Contracts.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>一次被记录的事件。</summary>
internal sealed record RecordedEvent(string EventName, object Payload);

/// <summary>记录所有事件，并支持按条件等待（测试里等 turn.completed / approval.request）。</summary>
internal sealed class RecordingTurnEventSink : ITurnEventSink
{
    private readonly object _gate = new();
    private readonly List<RecordedEvent> _events = [];
    private readonly List<(Func<RecordedEvent, bool> Match, TaskCompletionSource<RecordedEvent> Completion)> _waiters = [];

    public IReadOnlyList<RecordedEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToList();
            }
        }
    }

    public ValueTask PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        var recorded = new RecordedEvent(eventName, payload);
        List<TaskCompletionSource<RecordedEvent>> matched = [];
        lock (_gate)
        {
            _events.Add(recorded);
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Match(recorded))
                {
                    matched.Add(_waiters[i].Completion);
                    _waiters.RemoveAt(i);
                }
            }
        }

        foreach (var completion in matched)
        {
            completion.TrySetResult(recorded);
        }

        return default;
    }

    /// <summary>等待下一条匹配的事件（已有的事件先查）。</summary>
    public Task<RecordedEvent> WaitForAsync(
        Func<RecordedEvent, bool> predicate,
        TimeSpan? timeout = null)
    {
        TaskCompletionSource<RecordedEvent> completion;
        lock (_gate)
        {
            var existing = _events.FirstOrDefault(predicate);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            completion = new TaskCompletionSource<RecordedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((predicate, completion));
        }

        return completion.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(10));
    }

    /// <summary>按事件名取载荷。</summary>
    public IReadOnlyList<T> Payloads<T>(string eventName)
    {
        lock (_gate)
        {
            return _events
                .Where(e => string.Equals(e.EventName, eventName, StringComparison.Ordinal))
                .Select(e => (T)e.Payload)
                .ToList();
        }
    }
}
