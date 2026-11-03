namespace Shoyee.Storage.Abstractions;

/// <summary>
/// ⚠️ 预置仓储：暂无调用方，接口可能随对应模块（Agent 审批 / Channels / Proactive）调整。
/// </summary>
/// <summary>发送实测仓库（微信窗口期数据来源）。</summary>
public interface ISendAttemptRepository
{
    /// <summary>记一次发送结果。</summary>
    Task AddAsync(SendAttemptRecord record, CancellationToken cancellationToken = default);

    /// <summary>该 Lane 最近一次成功发送时间。</summary>
    Task<DateTimeOffset?> GetLastSuccessAsync(string laneId, CancellationToken cancellationToken = default);
}
