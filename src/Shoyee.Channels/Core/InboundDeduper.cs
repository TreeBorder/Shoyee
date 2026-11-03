namespace Shoyee.Channels.Core;

/// <summary>
/// 入站去重：渠道在重连/补拉时可能重复投递同一条消息。
///
/// 只用进程内定长集合（默认 1024 条，FIFO 淘汰）——重启窗口内的重复会漏过，
/// 但为它建一张表不值得：重复投递只会多跑一轮，不会破坏历史。
/// </summary>
public sealed class InboundDeduper
{
    private readonly int _capacity;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>创建去重器。</summary>
    /// <param name="capacity">最多记住多少条（按到达顺序淘汰）。</param>
    public InboundDeduper(int capacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>登记一条消息；返回 false = 重复。</summary>
    /// <param name="laneId">Lane id。</param>
    /// <param name="externalId">渠道消息 id。</param>
    public bool TryAdd(string laneId, string externalId)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            return true; // 渠道没给 id 就不要假装能去重
        }

        var key = laneId + "\n" + externalId;
        lock (_gate)
        {
            if (!_seen.Add(key))
            {
                return false;
            }

            _order.Enqueue(key);
            while (_order.Count > _capacity)
            {
                _seen.Remove(_order.Dequeue());
            }

            return true;
        }
    }
}
