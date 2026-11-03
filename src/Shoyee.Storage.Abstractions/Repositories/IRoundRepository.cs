using Microsoft.Extensions.AI;

namespace Shoyee.Storage.Abstractions;

/// <summary>
/// RP 轮次仓库：写一轮、读全局时间线窗口。<b>这是 RP 历史真源</b>。
///
/// 轮次号是 <b>RP 全局</b>单调序号（跨 Lane 共享），因为 RP 只有一条连续时间线；
/// <c>lane_id</c> 只记录这一轮从哪个渠道来，用于展示、投递与检索过滤。
/// 写入必须全局串行（<c>TurnService</c> 的 RP gate），否则两条 Lane 会抢到同一个轮次号。
/// </summary>
public interface IRoundRepository
{
    /// <summary>追加一轮（轮次号 = 全局最大 + 1），同时写消息投影与检索索引。</summary>
    Task<RoundRecord> AppendAsync(
        string laneId,
        IReadOnlyList<ChatMessage> request,
        IReadOnlyList<ChatMessage> response,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default);

    /// <summary>全局最近 <paramref name="count"/> 轮（升序，跨 Lane 混排）。</summary>
    Task<IReadOnlyList<RoundRecord>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default);

    /// <summary>全局 <paramref name="afterRoundNo"/> 之后的轮次（升序）。</summary>
    Task<IReadOnlyList<RoundRecord>> GetAfterAsync(
        int afterRoundNo,
        int count = 50,
        CancellationToken cancellationToken = default);

    /// <summary>全局当前最大轮次号（没有为 0）。摘要触发用它取模。</summary>
    Task<int> GetMaxRoundNoAsync(CancellationToken cancellationToken = default);

    /// <summary>RP 全局轮数统计。</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
