using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Contracts.Text;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Proactive.Cron;

/// <summary>
/// 一次 cron 触发的执行：认领 → 按 kind 产出 → 记台账 → 推进下次触发。
///
/// <list type="bullet">
/// <item><b>prompt</b>：工作模式跑在任务自己的独立会话（首次触发时建）；RP 在目标 Lane 跑真轮次。</item>
/// <item><b>script</b>：跑命令，stdout 即产出；RP 再把产出作为她的话追加到时间线。</item>
/// <item><b>message</b>：固定文本；RP 直接追加到时间线（不跑模型），工作模式只进执行台账。</item>
/// </list>
/// 产出不往日常对话里插：工作模式只在任务页/执行台账；需要叫醒时用 <c>deliver=push</c>。
/// </summary>
public sealed class CronRunner
{
    private readonly ICronRepository _jobs;
    private readonly ICronScheduler _scheduler;
    private readonly ICronScriptRunner _scripts;
    private readonly ISessionService _sessions;
    private readonly ITurnService _turns;
    private readonly ILaneRepository _lanes;
    private readonly IOutboundDelivery _delivery;
    private readonly INotificationPusher _pusher;
    private readonly ProactiveOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<CronRunner> _logger;

    /// <summary>创建执行器。</summary>
    /// <param name="jobs">cron 仓库。</param>
    /// <param name="scheduler">调度面（熔断停用后移除触发器）。</param>
    /// <param name="scripts">脚本执行器。</param>
    /// <param name="sessions">工作会话服务。</param>
    /// <param name="turns">轮次服务。</param>
    /// <param name="lanes">Lane 仓库（RP 投递目标）。</param>
    /// <param name="delivery">IM 投递台账。</param>
    /// <param name="pusher">Server酱推送。</param>
    /// <param name="config">工作区配置。</param>
    /// <param name="clock">时间源。</param>
    /// <param name="logger">日志。</param>
    public CronRunner(
        ICronRepository jobs,
        ICronScheduler scheduler,
        ICronScriptRunner scripts,
        ISessionService sessions,
        ITurnService turns,
        ILaneRepository lanes,
        IOutboundDelivery delivery,
        INotificationPusher pusher,
        WorkspaceConfig config,
        TimeProvider? clock = null,
        ILogger<CronRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(pusher);
        ArgumentNullException.ThrowIfNull(config);
        _jobs = jobs;
        _scheduler = scheduler;
        _scripts = scripts;
        _sessions = sessions;
        _turns = turns;
        _lanes = lanes;
        _delivery = delivery;
        _pusher = pusher;
        _options = config.Proactive;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CronRunner>.Instance;
    }

    /// <summary>执行一次触发（Quartz job 调这里；测试可直接调）。</summary>
    /// <param name="jobId">任务 id。</param>
    /// <param name="scheduledFor">本次计划触发时间（去重键）。</param>
    /// <param name="nextFireTimeUtc">Quartz 给的下次触发时间（null = 不再触发）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ExecuteAsync(
        string jobId,
        DateTimeOffset scheduledFor,
        DateTimeOffset? nextFireTimeUtc = null,
        CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || !job.Enabled)
        {
            return;
        }

        if (!await _jobs.TryClaimAsync(jobId, scheduledFor, cancellationToken).ConfigureAwait(false))
        {
            // 同一次触发已经被认领过（重启/重复 tick）：只推进展示用的下次时间
            await _jobs.SetNextRunAsync(jobId, nextFireTimeUtc, cancellationToken).ConfigureAwait(false);
            return;
        }

        var next = nextFireTimeUtc ?? NextFire(job, scheduledFor);
        try
        {
            var output = await RunBodyAsync(job, cancellationToken).ConfigureAwait(false);
            var result = await PushIfRequestedAsync(job, output, cancellationToken).ConfigureAwait(false);

            await _jobs.CompleteAsync(jobId, scheduledFor, "succeeded", result, cancellationToken)
                .ConfigureAwait(false);
            await _jobs.SetStateAsync(jobId, "succeeded", null, 0, cancellationToken).ConfigureAwait(false);
            await _jobs.SetNextRunAsync(jobId, next, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "cron 任务完成：{JobId} {Name}（{Kind}，{Length} 字）",
                job.Id,
                job.Name,
                job.Kind,
                result?.Length ?? 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _jobs.CompleteAsync(jobId, scheduledFor, "cancelled", "shutdown", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(job, scheduledFor, next, ex).ConfigureAwait(false);
        }
    }

    private async Task<string> RunBodyAsync(CronJobRecord job, CancellationToken cancellationToken)
    {
        var kind = job.Kind.Trim().ToLowerInvariant();
        return job.Mode switch
        {
            Mode.Roleplay => await RunRoleplayAsync(job, kind, cancellationToken).ConfigureAwait(false),
            _ => await RunWorkAsync(job, kind, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<string> RunWorkAsync(CronJobRecord job, string kind, CancellationToken cancellationToken)
        => kind switch
        {
            "message" => job.Payload,
            "script" => RequireOutput(await _scripts.RunAsync(job.Payload, cancellationToken).ConfigureAwait(false)),
            "prompt" => await RunWorkPromptAsync(job, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"未知的任务类型：{job.Kind}（工作模式支持 prompt / script / message）。"),
        };

    private async Task<string> RunRoleplayAsync(CronJobRecord job, string kind, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.LaneId))
        {
            throw new InvalidOperationException("RP 定时任务缺少目标 Lane（lane_id）。");
        }

        var lane = await _lanes.GetAsync(job.LaneId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"RP 定时任务的目标 Lane 不存在：{job.LaneId}。");
        if (lane.Mode != Mode.Roleplay)
        {
            throw new InvalidOperationException($"Lane {job.LaneId} 不是 RP Lane，不能作为定时问候的目标。");
        }

        if (kind == "prompt")
        {
            var outcome = await RunRoleplayPromptAsync(job, cancellationToken).ConfigureAwait(false);
            // prompt 轮次自己的事件/落库已经完成；这里只补 IM 回投（正文 + 她这轮产出的语音/图片/文件）
            var text = MessageText.CleanReply(outcome.Text);
            if (!string.IsNullOrWhiteSpace(text))
            {
                await DeliverToLaneAsync(lane, text, cancellationToken).ConfigureAwait(false);
            }

            await DeliverAttachmentsToLaneAsync(lane, outcome.Attachments, cancellationToken).ConfigureAwait(false);
            return text;
        }

        var output = kind switch
        {
            "message" => job.Payload,
            "script" => RequireOutput(await _scripts.RunAsync(job.Payload, cancellationToken).ConfigureAwait(false)),
            _ => throw new InvalidOperationException(
                $"未知的任务类型：{job.Kind}（RP 支持 prompt / script / message）。"),
        };

        if (!string.IsNullOrWhiteSpace(output))
        {
            // 固定消息/脚本产出不跑模型：直接追加成「她说的话」，桌面走 message.appended
            await _turns.AppendSpokenAsync(lane.Id, output, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await DeliverToLaneAsync(lane, output, cancellationToken).ConfigureAwait(false);
        }

        return output;
    }

    private async Task<string> RunWorkPromptAsync(CronJobRecord job, CancellationToken cancellationToken)
    {
        var sessionId = job.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = await CreateTaskSessionAsync(job, cancellationToken).ConfigureAwait(false);
            await _jobs.UpsertAsync(job with { SessionId = sessionId }, cancellationToken).ConfigureAwait(false);
        }

        TurnOutcome outcome;
        try
        {
            outcome = await SendWorkTurnAsync(sessionId, job.Payload, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Code is RpcErrors.NotFound or RpcErrors.SessionArchived)
        {
            // 任务会话被归档/删了：换一个新会话继续，不把任务卡死
            sessionId = await CreateTaskSessionAsync(job, cancellationToken).ConfigureAwait(false);
            await _jobs.UpsertAsync(job with { SessionId = sessionId }, cancellationToken).ConfigureAwait(false);
            outcome = await SendWorkTurnAsync(sessionId, job.Payload, cancellationToken).ConfigureAwait(false);
        }

        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException("定时任务的模型轮次失败（详见会话记录）。");
        }

        return outcome.Text;
    }

    private Task<TurnOutcome> SendWorkTurnAsync(string sessionId, string prompt, CancellationToken cancellationToken)
        => _turns.SendAndWaitAsync(
            new TurnSendRequest(prompt, SessionId: sessionId, SystemInput: true),
            cancellationToken);

    private async Task<TurnOutcome> RunRoleplayPromptAsync(CronJobRecord job, CancellationToken cancellationToken)
    {
        var outcome = await _turns
            .SendAndWaitAsync(
                new TurnSendRequest(job.Payload, LaneId: job.LaneId, SystemInput: true),
                cancellationToken)
            .ConfigureAwait(false);
        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException("定时问候的模型轮次失败。");
        }

        return outcome;
    }

    private async Task<string> CreateTaskSessionAsync(CronJobRecord job, CancellationToken cancellationToken)
    {
        var detail = await _sessions
            .CreateAsync(new SessionCreateRequest(Mode.Work, Title: $"定时任务：{job.Name}"), cancellationToken)
            .ConfigureAwait(false);
        return detail.Id;
    }

    private async Task DeliverToLaneAsync(LaneRecord lane, string text, CancellationToken cancellationToken)
    {
        if (lane.Channel == LaneChannel.Desktop || string.IsNullOrEmpty(lane.LastTarget))
        {
            return;
        }

        await _delivery
            .SendTextAsync(lane.Id, null, lane.LastTarget, text, "cron", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>RP prompt 轮次产出的附件：桌面靠 <c>turn.completed.attachments</c>，IM 在这里逐个传文件。</summary>
    private async Task DeliverAttachmentsToLaneAsync(
        LaneRecord lane,
        IReadOnlyList<MessageAttachment>? attachments,
        CancellationToken cancellationToken)
    {
        if (attachments is not { Count: > 0 }
            || lane.Channel == LaneChannel.Desktop
            || string.IsNullOrEmpty(lane.LastTarget))
        {
            return;
        }

        foreach (var attachment in attachments)
        {
            await _delivery
                .SendFileAsync(
                    lane.Id,
                    null,
                    lane.LastTarget,
                    attachment.Path,
                    caption: null,
                    kind: "cron",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string> PushIfRequestedAsync(CronJobRecord job, string output, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(output)
            || !DeliverTargets(job.Deliver).Contains("push", StringComparer.OrdinalIgnoreCase))
        {
            return output;
        }

        try
        {
            await _pusher.PushAsync(job.Name, output, cancellationToken).ConfigureAwait(false);
            return output;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "cron 任务产出已记录，但推送失败：{JobId} {Name}", job.Id, job.Name);
            return output + $"\n\n[通知失败] {ex.Message}";
        }
    }

    private async Task HandleFailureAsync(
        CronJobRecord job,
        DateTimeOffset scheduledFor,
        DateTimeOffset? next,
        Exception exception)
    {
        var streak = job.FailureStreak + 1;
        var tripped = streak >= Math.Max(1, _options.EffectiveCron.MaxFailureStreak);

        await _jobs.CompleteAsync(job.Id, scheduledFor, "failed", exception.Message, CancellationToken.None)
            .ConfigureAwait(false);
        await _jobs.SetStateAsync(job.Id, "failed", exception.Message, streak, CancellationToken.None)
            .ConfigureAwait(false);
        await _jobs.SetNextRunAsync(job.Id, tripped ? null : next, CancellationToken.None).ConfigureAwait(false);

        if (tripped)
        {
            await _jobs.SetEnabledAsync(job.Id, false, CancellationToken.None).ConfigureAwait(false);
            await _scheduler.RefreshAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
            _logger.LogError(
                exception,
                "cron 任务连续失败 {Streak} 次，已停用：{JobId} {Name}",
                streak,
                job.Id,
                job.Name);
        }
        else
        {
            _logger.LogWarning(exception, "cron 任务失败（{Streak} 连败）：{JobId} {Name}", streak, job.Id, job.Name);
        }
    }

    private DateTimeOffset? NextFire(CronJobRecord job, DateTimeOffset after)
    {
        try
        {
            return CronScheduleParser.GetNext(CronScheduleParser.Parse(job.Schedule), _clock.GetUtcNow());
        }
        catch (FormatException)
        {
            return after;
        }
    }

    private static string RequireOutput(CronScriptResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"脚本失败（exit {result.ExitCode}）：{result.Stderr.Trim()}");
        }

        return result.Stdout;
    }

    private static string[] DeliverTargets(string deliver)
        => string.IsNullOrWhiteSpace(deliver)
            ? []
            : deliver.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
