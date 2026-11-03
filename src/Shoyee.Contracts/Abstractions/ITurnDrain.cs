namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 轮次的收尾面：daemon 优雅退出时先等在跑/排队的轮次自己结束，超时再统一取消。
/// 与 <see cref="ITurnService"/> 分开，避免渠道/Handlers 拿到不属于它们的停机能力。
/// </summary>
public interface ITurnDrain
{
    /// <summary>当前未收束的轮次数（含排队中）。</summary>
    int ActiveTurnCount { get; }

    /// <summary>等到没有在跑/排队的轮次；到点返回 false。</summary>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>取消全部轮次（等审批的轮次算在跑，取消后按拒绝收束）。</summary>
    void CancelAll();
}
