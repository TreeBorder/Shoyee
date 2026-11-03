using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Desktop.Rpc;
using Shoyee.Desktop.ViewModels.Cron;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>定时任务页：列表、执行记录、启停与删除（用假网关）。</summary>
public sealed class CronViewModelTests
{
    [Fact]
    public void LoadsJobsAndSelectsFirstWithExecutions()
    {
        var gateway = new FakeCronGateway();
        gateway.Jobs.Add(Job("cron_1", "每日简报", "0 9 * * ?", DateTimeOffset.Now.AddHours(2)));
        gateway.Jobs.Add(Job("cron_2", "每周总结", "every:7d", null, enabled: false));
        gateway.Details["cron_1"] = (
            gateway.Jobs[0],
            [
                new CronExecutionDto(
                    "cron_1",
                    DateTimeOffset.Now.AddHours(-22),
                    DateTimeOffset.Now.AddHours(-22),
                    DateTimeOffset.Now.AddHours(-22).AddSeconds(12),
                    "succeeded",
                    "今天有三件事。"),
            ]);

        var viewModel = new CronViewModel();
        viewModel.AttachGateway(gateway);

        Assert.True(viewModel.IsLive);
        Assert.Equal(2, viewModel.Jobs.Count);
        Assert.Equal("cron_1", viewModel.SelectedJob!.Id);
        Assert.True(viewModel.HasJobs);
        Assert.False(viewModel.IsEmpty);
        var execution = Assert.Single(viewModel.Executions);
        Assert.Equal("成功", execution.StateLabel);
        Assert.Equal("今天有三件事。", execution.ResultText);
        Assert.Contains("2 个", viewModel.HeaderSubtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void TogglesSelectedJobAndReloadsList()
    {
        var gateway = new FakeCronGateway();
        gateway.Jobs.Add(Job("cron_1", "每日简报", "0 9 * * ?", DateTimeOffset.Now.AddHours(2)));
        gateway.Details["cron_1"] = (gateway.Jobs[0], []);

        var viewModel = new CronViewModel();
        viewModel.AttachGateway(gateway);

        viewModel.ToggleEnabledCommand.Execute(null);

        Assert.Equal(("cron_1", false), Assert.Single(gateway.EnabledCalls));
        Assert.False(viewModel.SelectedJob!.Enabled);
        Assert.Equal("停用", viewModel.SelectedJob.EnabledLabel);
    }

    [Fact]
    public void DeleteRequiresConfirmation()
    {
        var gateway = new FakeCronGateway();
        gateway.Jobs.Add(Job("cron_1", "每日简报", "0 9 * * ?", DateTimeOffset.Now.AddHours(2)));
        gateway.Details["cron_1"] = (gateway.Jobs[0], []);

        var viewModel = new CronViewModel();
        viewModel.AttachGateway(gateway);

        viewModel.RequestDeleteCommand.Execute(null);
        Assert.True(viewModel.IsConfirmingDelete);
        Assert.Empty(gateway.Removed);

        viewModel.CancelDeleteCommand.Execute(null);
        Assert.False(viewModel.IsConfirmingDelete);
        Assert.Empty(gateway.Removed);

        viewModel.RequestDeleteCommand.Execute(null);
        viewModel.ConfirmDeleteCommand.Execute(null);

        Assert.Equal("cron_1", Assert.Single(gateway.Removed));
        Assert.Empty(viewModel.Jobs);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasSelection);
    }

    [Fact]
    public void FormatsSchedulesAndTimes()
    {
        Assert.Equal("每 30 分钟", CronText.Schedule("every:30m"));
        Assert.Equal("每 2 小时", CronText.Schedule("every:2h"));
        Assert.StartsWith("一次性 ·", CronText.Schedule("once:2026-11-03T09:00:00+08:00"), StringComparison.Ordinal);
        Assert.Equal("cron · 0 9 * * ?", CronText.Schedule("0 9 * * ?"));
        Assert.Equal("跑模型", CronText.Kind("prompt"));
        Assert.Equal("推送通知 · 只记台账", CronText.Deliver("push,local"));
        Assert.Equal("未运行", CronText.Status(null));
        Assert.Equal("—", CronText.NextRun(null));
        Assert.Equal("—", CronText.Duration(null, null));
    }

    private static CronJobDto Job(
        string id,
        string name,
        string schedule,
        DateTimeOffset? nextRunAt,
        bool enabled = true) => new(
        id,
        name,
        schedule,
        "prompt",
        "写一份今天的简报。",
        "local",
        enabled,
        Mode.Work,
        null,
        null,
        nextRunAt,
        null,
        null,
        null,
        0,
        DateTimeOffset.Now.AddDays(-3));

    private sealed class FakeCronGateway : ICronGateway
    {
        public bool IsConnected { get; set; } = true;

        public List<CronJobDto> Jobs { get; } = [];

        public Dictionary<string, (CronJobDto Job, IReadOnlyList<CronExecutionDto> Executions)> Details { get; } = [];

        public List<(string JobId, bool Enabled)> EnabledCalls { get; } = [];

        public List<string> Removed { get; } = [];

        public Task<IReadOnlyList<CronJobDto>> ListAsync(
            bool includeDisabled = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CronJobDto>>(Jobs.ToList());

        public Task<CronJobDetail> GetAsync(
            string jobId,
            int executions = 10,
            CancellationToken cancellationToken = default)
        {
            if (!Details.TryGetValue(jobId, out var detail))
            {
                detail = (Jobs.First(job => job.Id == jobId), []);
            }

            return Task.FromResult(new CronJobDetail(detail.Job, detail.Executions));
        }

        public Task SetEnabledAsync(
            string jobId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            EnabledCalls.Add((jobId, enabled));
            var index = Jobs.FindIndex(job => job.Id == jobId);
            if (index >= 0)
            {
                Jobs[index] = Jobs[index] with { Enabled = enabled };
                if (Details.TryGetValue(jobId, out var detail))
                {
                    Details[jobId] = (detail.Job with { Enabled = enabled }, detail.Executions);
                }
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string jobId, CancellationToken cancellationToken = default)
        {
            Removed.Add(jobId);
            Jobs.RemoveAll(job => job.Id == jobId);
            Details.Remove(jobId);
            return Task.CompletedTask;
        }
    }
}
