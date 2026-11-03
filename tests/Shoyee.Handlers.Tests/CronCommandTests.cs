using Shoyee.Contracts.Domain;
using Shoyee.Handlers.Commands;
using Shoyee.Handlers.Tests.TestSupport;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary><c>/cron</c>：只读、按模式过滤、带停用项、调度写库里原文。</summary>
public sealed class CronCommandTests
{
    [Fact]
    public async Task ListsCurrentModeIncludingDisabled()
    {
        var jobs = new FakeCronRepository();
        jobs.Jobs.AddRange(
        [
            Job("cron_1", "早报", "0 9 * * ?", Mode.Work, enabled: true, lastStatus: "succeeded"),
            Job("cron_2", "喝水提醒", "every:2h", Mode.Work, enabled: false),
            Job("cron_3", "晚间问候", "0 22 * * ?", Mode.Roleplay, enabled: true, lastStatus: "failed", lastError: "模型超时"),
        ]);

        var reply = await new CronCommand(jobs)
            .HandleAsync(new CommandContext("mm:work", "ses_a", "list", Mode.Work));

        Assert.False(jobs.LastEnabledOnly);
        Assert.Contains("| 任务 | 调度 | 下次 | 状态 |", reply);
        Assert.Contains("| 早报 | 0 9 * * ? |", reply);
        Assert.Contains("| 喝水提醒 | every:2h |", reply);
        Assert.Contains("⏸ 已停用", reply);
        Assert.DoesNotContain("晚间问候", reply);
    }

    [Fact]
    public async Task FailedJobShowsError()
    {
        var jobs = new FakeCronRepository();
        jobs.Jobs.Add(Job("cron_1", "早报", "0 9 * * ?", Mode.Roleplay, enabled: true, lastStatus: "failed", lastError: "模型超时"));

        var reply = await new CronCommand(jobs)
            .HandleAsync(new CommandContext("mm:roleplay", "mm:roleplay", string.Empty, Mode.Roleplay));

        Assert.Contains("⚠️ 早报：模型超时", reply);
    }

    [Fact]
    public async Task EmptySaysSo()
    {
        var reply = await new CronCommand(new FakeCronRepository())
            .HandleAsync(new CommandContext("mm:work", "ses_a", string.Empty, Mode.Work));

        Assert.Contains("还没有定时任务", reply);
    }

    [Fact]
    public async Task UnknownSubcommand_ShowsUsage()
    {
        var jobs = new FakeCronRepository();

        var reply = await new CronCommand(jobs)
            .HandleAsync(new CommandContext("mm:work", "ses_a", "remove cron_1", Mode.Work));

        Assert.Contains("用法", reply);
        Assert.Null(jobs.LastEnabledOnly);
    }

    private static CronJobRecord Job(
        string id,
        string name,
        string schedule,
        Mode mode,
        bool enabled,
        string? lastStatus = null,
        string? lastError = null)
        => new(
            id,
            name,
            schedule,
            "prompt",
            "跑一下",
            "local",
            enabled,
            LastRunAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Mode: mode,
            NextRunAt: enabled ? DateTimeOffset.UtcNow.AddHours(1) : null,
            LastStatus: lastStatus,
            LastError: lastError);
}
