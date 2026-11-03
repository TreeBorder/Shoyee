using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Dtos;

/// <summary>定时任务（桌面任务页的列表项/详情头）。</summary>
/// <param name="Id">任务 id。</param>
/// <param name="Name">名字。</param>
/// <param name="Schedule">调度串（<c>once:</c> / <c>every:</c> / Quartz cron）。</param>
/// <param name="Kind">执行方式：prompt / script / message。</param>
/// <param name="Payload">提示词、命令或固定文本。</param>
/// <param name="Deliver">附加投递：local / push，可逗号组合。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="Mode">所属模式。</param>
/// <param name="LaneId">RP 任务的投递 Lane（工作模式为 null）。</param>
/// <param name="SessionId">工作模式 prompt 任务的执行会话（首次触发后才有值）。</param>
/// <param name="NextRunAt">下次触发时间（展示用；实际调度在 Quartz 内存里）。</param>
/// <param name="LastRunAt">最近运行时间。</param>
/// <param name="LastStatus">上次状态（succeeded / failed）。</param>
/// <param name="LastError">上次失败原因。</param>
/// <param name="FailureStreak">连续失败次数（达到阈值自动停用）。</param>
/// <param name="CreatedAt">创建时间。</param>
public sealed record CronJobDto(
    string Id,
    string Name,
    string Schedule,
    string Kind,
    string Payload,
    string Deliver,
    bool Enabled,
    Mode Mode,
    string? LaneId,
    string? SessionId,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastStatus,
    string? LastError,
    int FailureStreak,
    DateTimeOffset CreatedAt);

/// <summary>一条执行记录（任务页的「单次执行结果」）。</summary>
/// <param name="JobId">任务 id。</param>
/// <param name="ScheduledFor">本次触发计划时间（UTC，去重键的一半）。</param>
/// <param name="StartedAt">开始时间。</param>
/// <param name="FinishedAt">结束时间（还在跑时为 null）。</param>
/// <param name="State">running / succeeded / failed。</param>
/// <param name="Result">产出正文或错误说明。</param>
public sealed record CronExecutionDto(
    string JobId,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string State,
    string? Result);

/// <summary><c>cron.list</c> 的请求。</summary>
/// <param name="IncludeDisabled">是否包含已停用的任务（默认 true）。</param>
public sealed record CronListRequest(bool IncludeDisabled = true);

/// <summary><c>cron.list</c> 的返回。</summary>
/// <param name="Items">任务（按下次触发时间排序，没有的排最后）。</param>
public sealed record CronListResult(IReadOnlyList<CronJobDto> Items);

/// <summary><c>cron.get</c> 的请求。</summary>
/// <param name="JobId">任务 id。</param>
/// <param name="Executions">附带最近几条执行记录（默认 10，最多 50）。</param>
public sealed record CronGetRequest(string JobId, int Executions = 10);

/// <summary><c>cron.get</c> 的返回：任务定义 + 最近执行。</summary>
/// <param name="Job">任务定义。</param>
/// <param name="Executions">最近执行（按计划时间倒序）。</param>
public sealed record CronJobDetail(CronJobDto Job, IReadOnlyList<CronExecutionDto> Executions);

/// <summary><c>cron.executions</c> 的请求。</summary>
/// <param name="JobId">任务 id。</param>
/// <param name="Limit">条数（默认 20，最多 100）。</param>
public sealed record CronExecutionsRequest(string JobId, int Limit = 20);

/// <summary><c>cron.executions</c> 的返回。</summary>
/// <param name="Items">执行记录（按计划时间倒序）。</param>
public sealed record CronExecutionsResult(IReadOnlyList<CronExecutionDto> Items);

/// <summary><c>cron.setEnabled</c> 的请求。</summary>
/// <param name="JobId">任务 id。</param>
/// <param name="Enabled">true = 启用，false = 停用。</param>
public sealed record CronSetEnabledRequest(string JobId, bool Enabled);

/// <summary><c>cron.remove</c> 的请求。</summary>
/// <param name="JobId">任务 id。</param>
public sealed record CronRemoveRequest(string JobId);
