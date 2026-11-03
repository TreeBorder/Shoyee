using Shoyee.Contracts.Dtos;

namespace Shoyee.Desktop.Rpc;

/// <summary>
/// 定时任务页需要的 RPC 面（工作角色）。任务只能由模型用 cron 工具创建，
/// 桌面只做查看、启停和删除；RP 任务不在这里暴露。
/// </summary>
public interface ICronGateway
{
    /// <summary>工作连接是否可用。</summary>
    bool IsConnected { get; }

    /// <summary>任务列表（按下次触发时间排序）。</summary>
    /// <param name="includeDisabled">是否包含已停用的任务。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<CronJobDto>> ListAsync(
        bool includeDisabled = true,
        CancellationToken cancellationToken = default);

    /// <summary>任务详情 + 最近执行记录。</summary>
    /// <param name="jobId">任务 id。</param>
    /// <param name="executions">附带几条执行记录（1–50）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<CronJobDetail> GetAsync(
        string jobId,
        int executions = 10,
        CancellationToken cancellationToken = default);

    /// <summary>启停任务。</summary>
    /// <param name="jobId">任务 id。</param>
    /// <param name="enabled">true = 启用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>删除任务（含执行台账）。</summary>
    /// <param name="jobId">任务 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RemoveAsync(string jobId, CancellationToken cancellationToken = default);
}
