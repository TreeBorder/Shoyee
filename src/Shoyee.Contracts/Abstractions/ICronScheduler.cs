namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// cron 调度面：工具/桌面改完任务后让调度器按库里的最新定义重建触发器。
/// 实现是 Quartz（Proactive），调用方不接触 Quartz 类型。
/// </summary>
public interface ICronScheduler
{
    /// <summary>校验调度串；合法返回 null，非法返回一句可操作的说明。</summary>
    /// <param name="schedule">调度串（<c>once:</c> / <c>every:</c> / Quartz cron）。</param>
    string? Validate(string schedule);

    /// <summary>
    /// 按库里的定义刷新触发器：<paramref name="jobId"/> 为空 = 重载全部。
    /// 任务被停用/删除时对应触发器会一起移除。
    /// </summary>
    /// <param name="jobId">任务 id；null = 全部。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RefreshAsync(string? jobId = null, CancellationToken cancellationToken = default);
}
