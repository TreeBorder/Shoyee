using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Shoyee.Contracts.Options;
using Shoyee.Proactive.Cron;
using Shoyee.Proactive.Tests.TestSupport;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Proactive.Tests.Cron;

public sealed class CronJobSchedulerTests
{
    [Fact]
    public async Task Refresh_SchedulesEnabledJob_AndRemovesDisabledOne()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz();
        await using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();
        await scheduler.Start();

        var jobs = new FakeCronRepository();
        await jobs.UpsertAsync(new CronJobRecord(
            "cron_x",
            "每半小时",
            "every:30m",
            "message",
            "hi",
            "local",
            Enabled: true,
            LastRunAt: null,
            CreatedAt: DateTimeOffset.UtcNow));

        var config = new WorkspaceConfig(
            Path.GetTempPath(),
            new ModelProfiles(
                new ModelEndpoint("http://localhost:1/v1", "main", "MAIN_KEY"),
                new ModelEndpoint("http://localhost:1/v1", "small", "SMALL_KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions(Enabled: false)),
            new ToolOptions());

        var subject = new CronJobScheduler(factory, jobs, config);
        await subject.RefreshAsync();

        var trigger = await scheduler.GetTrigger(new TriggerKey("cron_x", CronJobScheduler.Group));
        Assert.NotNull(trigger);
        Assert.NotNull(trigger!.NextFireTimeUtc);
        Assert.True(trigger.NextFireTimeUtc > DateTimeOffset.UtcNow.AddMinutes(25));
        Assert.NotNull(jobs.Rows["cron_x"].NextRunAt);

        await jobs.SetEnabledAsync("cron_x", false);
        await subject.RefreshAsync("cron_x");

        Assert.Null(await scheduler.GetTrigger(new TriggerKey("cron_x", CronJobScheduler.Group)));
        await scheduler.Shutdown();
    }
}
