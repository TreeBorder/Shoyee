namespace Shoyee.Storage.Abstractions;

/// <summary>
/// RP 摘要仓库（全局累积全文）。
///
/// RP 只有一条跨 Lane 的时间线，所以摘要也只有一份；表中仍保留 <c>scope</c>/<c>target_id</c>
/// 两列（<c>rp</c> / <c>roleplay</c>），但对外不再暴露 Lane。
/// </summary>
public interface ISummaryRepository
{
    /// <summary>取最新一条摘要；没有返回 null。</summary>
    Task<SummaryRecord?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>追加一条新摘要（带上覆盖到的全局轮次与本次轮数）。</summary>
    Task<SummaryRecord> AppendAsync(
        int throughRound,
        int roundCount,
        string content,
        CancellationToken cancellationToken = default);
}
