using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Composition;

namespace Shoyee.Tools.Cron;

/// <summary>
/// cron 工具族：<c>cron_add</c> / <c>cron_list</c> / <c>get_cron_detail</c> / <c>cron_pause</c> / <c>cron_remove</c>。
///
/// 工作模式任务跑在独立会话，产出只进执行台账（任务页可看）；需要叫醒用户时加 <c>deliver=push</c>。
/// RP 任务（如定时问候）把产出追加到目标 Lane：<c>prompt</c> 跑一轮模型，<c>message</c>/<c>script</c>
/// 直接当「她说的话」。RP 不允许建脚本任务（没有审批中间件，不给任意命令通道）。
/// </summary>
public static class CronTool
{
    /// <summary>新增任务。</summary>
    public const string AddToolName = "cron_add";

    /// <summary>列任务。</summary>
    public const string ListToolName = "cron_list";

    /// <summary>读任务详情（含执行结果/任务会话）。</summary>
    public const string DetailToolName = "get_cron_detail";

    /// <summary>启停任务。</summary>
    public const string PauseToolName = "cron_pause";

    /// <summary>删除任务。</summary>
    public const string RemoveToolName = "cron_remove";

    /// <summary>构建五个 cron 工具。</summary>
    /// <param name="jobs">cron 仓库。</param>
    /// <param name="scheduler">调度面（改完任务后刷新触发器）。</param>
    /// <param name="messages">消息投影仓库（读任务会话）。</param>
    /// <param name="mode">当前工具面所属模式。</param>
    /// <param name="maxJobs">任务数量上限。</param>
    public static IReadOnlyList<ToolDescriptor> Create(
        ICronRepository jobs,
        ICronScheduler scheduler,
        IMessageRepository? messages,
        Mode mode,
        int maxJobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(scheduler);

        var add = AIFunctionFactory.Create(
            async (
                [Description("任务名（短、能一眼看出干什么）。")] string name,
                [Description("调度：once:2026-10-30T09:00:00+08:00 / every:30m / Quartz cron（如 0 9 * * ?）。")] string schedule,
                [Description("执行方式：prompt=跑模型；script=跑命令（仅工作模式）；message=固定文本。")] string kind,
                [Description("提示词 / 命令 / 固定文本。")] string payload,
                [Description("附加投递：local（只记台账，默认）/ push（Server酱通知）/ local,push。")] string deliver = "local",
                [Description("RP 任务的投递 Lane（省略 = 当前 Lane）。工作模式忽略。")] string? laneId = null,
                CancellationToken cancellationToken = default) =>
            {
                var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
                if (normalizedKind is not ("prompt" or "script" or "message"))
                {
                    return "kind 只能是 prompt / script / message。";
                }

                if (mode == Mode.Roleplay && normalizedKind == "script")
                {
                    return "RP 模式不允许脚本任务（没有审批通道）；改用 prompt 或 message。";
                }

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(payload))
                {
                    return "name 和 payload 不能为空。";
                }

                var scheduleError = scheduler.Validate(schedule);
                if (scheduleError is not null)
                {
                    return "调度不合法：" + scheduleError;
                }

                var all = await jobs.ListAsync(enabledOnly: false, cancellationToken).ConfigureAwait(false);
                if (all.Count >= Math.Max(1, maxJobs))
                {
                    return $"任务数量已达上限（{maxJobs}），先删掉不用的任务。";
                }

                var now = DateTimeOffset.UtcNow;
                var record = new CronJobRecord(
                    Id: "cron_" + Guid.NewGuid().ToString("N")[..8],
                    Name: name.Trim(),
                    Schedule: schedule.Trim(),
                    Kind: normalizedKind,
                    Payload: payload,
                    Deliver: NormalizeDeliver(deliver),
                    Enabled: true,
                    LastRunAt: null,
                    CreatedAt: now,
                    Mode: mode,
                    LaneId: mode == Mode.Roleplay
                        ? (string.IsNullOrWhiteSpace(laneId) ? CronScope.LaneId ?? "desktop:roleplay" : laneId.Trim())
                        : null);

                var saved = await jobs.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
                await scheduler.RefreshAsync(saved.Id, cancellationToken).ConfigureAwait(false);

                return $"定时任务已创建：{saved.Id}\n"
                       + $"名称：{saved.Name}\n"
                       + $"模式：{(saved.Mode == Mode.Roleplay ? "RP" : "工作")}\n"
                       + $"调度：{saved.Schedule}\n"
                       + $"类型：{saved.Kind}\n"
                       + $"下次触发：{FormatTime(saved.NextRunAt)}";
            },
            new AIFunctionFactoryOptions
            {
                Name = AddToolName,
                Description = "新增定时任务。工作模式用于搜集/总结/定时汇报；RP 模式可做定时问候（message=固定话，prompt=她现写）。产出在定时任务页/执行台账，不往日常对话里插；要主动通知就加 deliver=push。",
            });

        var list = AIFunctionFactory.Create(
            async (
                [Description("是否包含已停用的任务（默认 true）。")] bool includeDisabled = true,
                CancellationToken cancellationToken = default) =>
            {
                var all = await jobs.ListAsync(enabledOnly: !includeDisabled, cancellationToken).ConfigureAwait(false);
                if (all.Count == 0)
                {
                    return includeDisabled ? "还没有定时任务。" : "没有启用的定时任务。";
                }

                var text = new StringBuilder($"共 {all.Count} 个定时任务：\n");
                foreach (var job in all)
                {
                    text.Append("- ")
                        .Append(job.Id).Append("｜").Append(job.Name)
                        .Append("｜").Append(job.Mode == Mode.Roleplay ? "RP" : "工作")
                        .Append("｜").Append(job.Kind)
                        .Append("｜").Append(job.Schedule)
                        .Append("｜").Append(job.Enabled ? "启用" : "停用")
                        .Append("｜下次 ").Append(FormatTime(job.NextRunAt))
                        .Append("｜上次 ").Append(job.LastStatus ?? "未运行")
                        .AppendLine();
                }

                return text.ToString().TrimEnd();
            },
            new AIFunctionFactoryOptions
            {
                Name = ListToolName,
                Description = "列出定时任务（id、模式、类型、调度、启停、下次触发、上次状态）。要看某次产出用 get_cron_detail。",
            });

        var detail = AIFunctionFactory.Create(
            async (
                [Description("任务 id（cron_list 或桌面 @ 引用返回的 id）。")] string jobId,
                [Description("附带最近几条执行结果（默认 5，最多 20）。")] int executions = 5,
                [Description("是否附带任务会话的最近消息（工作模式 prompt 任务才有）。")] bool includeSession = true,
                CancellationToken cancellationToken = default) =>
            {
                var job = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
                if (job is null)
                {
                    return $"没有找到定时任务 {jobId}。";
                }

                var text = new StringBuilder();
                text.AppendLine($"# {job.Name}（{job.Id}）")
                    .AppendLine($"模式：{(job.Mode == Mode.Roleplay ? "RP" : "工作")}")
                    .AppendLine($"类型：{job.Kind}")
                    .AppendLine($"调度：{job.Schedule}")
                    .AppendLine($"状态：{(job.Enabled ? "启用" : "停用")}；上次 {job.LastStatus ?? "未运行"}")
                    .AppendLine($"上次运行：{FormatTime(job.LastRunAt)}；下次触发：{FormatTime(job.NextRunAt)}")
                    .AppendLine($"投递：{(string.IsNullOrWhiteSpace(job.Deliver) ? "local" : job.Deliver)}");
                if (!string.IsNullOrEmpty(job.LaneId))
                {
                    text.AppendLine($"目标 Lane：{job.LaneId}");
                }

                if (!string.IsNullOrEmpty(job.LastError))
                {
                    text.AppendLine($"上次错误：{job.LastError}");
                }

                text.AppendLine().AppendLine("## 任务内容").AppendLine(job.Payload);

                var runs = await jobs
                    .ListExecutionsAsync(job.Id, Math.Clamp(executions, 1, 20), cancellationToken)
                    .ConfigureAwait(false);
                text.AppendLine().AppendLine("## 最近执行");
                if (runs.Count == 0)
                {
                    text.AppendLine("（还没有执行过）");
                }
                else
                {
                    foreach (var run in runs)
                    {
                        text.AppendLine($"### {FormatTime(run.ScheduledFor)} · {run.State}")
                            .AppendLine(string.IsNullOrWhiteSpace(run.Result) ? "（无产出）" : run.Result)
                            .AppendLine();
                    }
                }

                if (includeSession && job.Mode == Mode.Work && !string.IsNullOrEmpty(job.SessionId) && messages is not null)
                {
                    var rows = await messages
                        .GetPageAsync(sessionId: job.SessionId, limit: 20, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    text.AppendLine("## 任务会话").AppendLine($"session_id = {job.SessionId}");
                    if (rows.Count == 0)
                    {
                        text.AppendLine("（会话里还没有消息）");
                    }
                    else
                    {
                        foreach (var row in rows)
                        {
                            if (row.Kind is MessageKind.Text or MessageKind.Spoken)
                            {
                                text.AppendLine($"{row.CreatedAt:yyyy-MM-dd HH:mm} {row.Role}: {row.Content}");
                            }
                        }
                    }
                }

                return text.ToString().TrimEnd();
            },
            new AIFunctionFactoryOptions
            {
                Name = DetailToolName,
                Description = "读取一个定时任务的完整详情：定义、最近几次执行结果、以及任务会话的最近消息。桌面 @ 了某个定时任务、或 cron_list 里看到 id 时用它。",
            });

        var pause = AIFunctionFactory.Create(
            async (
                [Description("任务 id。")] string jobId,
                [Description("true=启用，false=停用。")] bool enabled,
                CancellationToken cancellationToken = default) =>
            {
                var changed = await jobs.SetEnabledAsync(jobId, enabled, cancellationToken).ConfigureAwait(false);
                if (!changed)
                {
                    return $"没有找到定时任务 {jobId}。";
                }

                await scheduler.RefreshAsync(jobId, cancellationToken).ConfigureAwait(false);
                return enabled ? $"定时任务 {jobId} 已启用。" : $"定时任务 {jobId} 已停用（触发器已移除）。";
            },
            new AIFunctionFactoryOptions
            {
                Name = PauseToolName,
                Description = "启用或停用一个定时任务（停用会移除触发器，保留定义与历史结果）。",
            });

        var remove = AIFunctionFactory.Create(
            async (
                [Description("任务 id。")] string jobId,
                CancellationToken cancellationToken = default) =>
            {
                var removed = await jobs.DeleteAsync(jobId, cancellationToken).ConfigureAwait(false);
                await scheduler.RefreshAsync(jobId, cancellationToken).ConfigureAwait(false);
                return removed ? $"定时任务 {jobId} 已删除（含执行台账）。" : $"没有找到定时任务 {jobId}。";
            },
            new AIFunctionFactoryOptions
            {
                Name = RemoveToolName,
                Description = "删除一个定时任务及其执行台账；只是暂时不用请用 cron_pause。",
            });

        return
        [
            ToolDescriptor.Create(add, ToolScope.All, writes: true, requiresApproval: true),
            ToolDescriptor.Create(list, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(detail, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(pause, ToolScope.All, writes: true, requiresApproval: true),
            ToolDescriptor.Create(remove, ToolScope.All, writes: true, requiresApproval: true),
        ];
    }

    private static string NormalizeDeliver(string deliver)
    {
        var targets = (deliver ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static t => t.ToLowerInvariant())
            .Where(static t => t is "local" or "push")
            .Distinct()
            .ToList();
        return targets.Count == 0 ? "local" : string.Join(',', targets);
    }

    private static string FormatTime(DateTimeOffset? value)
        => value is null ? "—" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
