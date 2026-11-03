using Microsoft.Extensions.AI;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Proactive.Cron;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Cron;
using Xunit;

namespace Shoyee.Tools.Tests.Cron;

public sealed class CronToolTests
{
    [Fact]
    public async Task Add_CreatesJobAndRefreshesScheduler()
    {
        var jobs = new FakeCronRepository();
        var scheduler = new FakeScheduler();
        var tools = CronTool.Create(jobs, scheduler, messages: null, Mode.Work, maxJobs: 5);
        var add = Find(tools, CronTool.AddToolName);

        var result = await add.InvokeAsync(new AIFunctionArguments
        {
            ["name"] = "每日汇报",
            ["schedule"] = "0 9 * * ?",
            ["kind"] = "prompt",
            ["payload"] = "整理今天的消息",
        });

        var text = result?.ToString() ?? string.Empty;
        Assert.Contains("已创建", text, StringComparison.Ordinal);
        var job = Assert.Single(jobs.Rows.Values);
        Assert.Equal(Mode.Work, job.Mode);
        Assert.Equal("prompt", job.Kind);
        Assert.Contains(job.Id, scheduler.Refreshed);
    }

    [Fact]
    public async Task Roleplay_RejectsScriptJobs()
    {
        var jobs = new FakeCronRepository();
        var tools = CronTool.Create(jobs, new FakeScheduler(), messages: null, Mode.Roleplay, maxJobs: 5);
        var add = Find(tools, CronTool.AddToolName);

        var result = await add.InvokeAsync(new AIFunctionArguments
        {
            ["name"] = "问候",
            ["schedule"] = "every:1h",
            ["kind"] = "script",
            ["payload"] = "say hi",
        });

        Assert.Contains("不允许脚本", result?.ToString(), StringComparison.Ordinal);
        Assert.Empty(jobs.Rows);
    }

    [Fact]
    public async Task Detail_IncludesPayloadAndExecutions()
    {
        var jobs = new FakeCronRepository();
        await jobs.UpsertAsync(new CronJobRecord(
            "cron_x",
            "问候",
            "every:1h",
            "message",
            "早上好",
            "local",
            true,
            null,
            DateTimeOffset.UtcNow,
            Mode.Roleplay,
            "desktop:roleplay"));
        var scheduled = DateTimeOffset.UtcNow;
        await jobs.TryClaimAsync("cron_x", scheduled);
        await jobs.CompleteAsync("cron_x", scheduled, "succeeded", "早上好呀");

        var tools = CronTool.Create(jobs, new FakeScheduler(), messages: null, Mode.Roleplay, maxJobs: 5);
        var detail = Find(tools, CronTool.DetailToolName);

        var result = await detail.InvokeAsync(new AIFunctionArguments { ["jobId"] = "cron_x" });
        var text = result?.ToString() ?? string.Empty;

        Assert.Contains("早上好", text, StringComparison.Ordinal);
        Assert.Contains("早上好呀", text, StringComparison.Ordinal);
    }

    private static AIFunction Find(IReadOnlyList<Shoyee.Tools.Composition.ToolDescriptor> tools, string name)
        => Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools, t => t.Metadata.Name == name).Tool);

    private sealed class FakeScheduler : ICronScheduler
    {
        public List<string?> Refreshed { get; } = [];

        public string? Validate(string schedule) => CronScheduleParser.Validate(schedule);

        public Task RefreshAsync(string? jobId = null, CancellationToken cancellationToken = default)
        {
            Refreshed.Add(jobId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCronRepository : ICronRepository
    {
        public Dictionary<string, CronJobRecord> Rows { get; } = new(StringComparer.Ordinal);

        private readonly List<CronExecutionRecord> _executions = [];
        private readonly HashSet<(string, DateTimeOffset)> _claims = [];

        public Task<IReadOnlyList<CronJobRecord>> ListAsync(bool enabledOnly = true, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CronJobRecord>>(Rows.Values.Where(j => !enabledOnly || j.Enabled).ToList());

        public Task<CronJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.TryGetValue(jobId, out var job) ? job : null);

        public Task<CronJobRecord> UpsertAsync(CronJobRecord job, CancellationToken cancellationToken = default)
        {
            Rows[job.Id] = job;
            return Task.FromResult(job);
        }

        public Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.Remove(jobId));

        public Task<bool> SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken = default)
        {
            if (!Rows.TryGetValue(jobId, out var job))
            {
                return Task.FromResult(false);
            }

            Rows[jobId] = job with { Enabled = enabled };
            return Task.FromResult(true);
        }

        public Task SetNextRunAsync(string jobId, DateTimeOffset? nextRunAt, CancellationToken cancellationToken = default)
        {
            if (Rows.TryGetValue(jobId, out var job))
            {
                Rows[jobId] = job with { NextRunAt = nextRunAt };
            }

            return Task.CompletedTask;
        }

        public Task SetStateAsync(string jobId, string? lastStatus, string? lastError, int failureStreak, CancellationToken cancellationToken = default)
        {
            if (Rows.TryGetValue(jobId, out var job))
            {
                Rows[jobId] = job with { LastStatus = lastStatus, LastError = lastError, FailureStreak = failureStreak };
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryClaimAsync(string jobId, DateTimeOffset scheduledFor, CancellationToken cancellationToken = default)
        {
            if (!_claims.Add((jobId, scheduledFor)))
            {
                return Task.FromResult(false);
            }

            _executions.Add(new CronExecutionRecord(jobId, scheduledFor, DateTimeOffset.UtcNow, null, "running", null));
            return Task.FromResult(true);
        }

        public Task CompleteAsync(string jobId, DateTimeOffset scheduledFor, string state, string? result, CancellationToken cancellationToken = default)
        {
            var index = _executions.FindIndex(e => e.JobId == jobId && e.ScheduledFor == scheduledFor);
            if (index >= 0)
            {
                _executions[index] = _executions[index] with { State = state, Result = result, FinishedAt = DateTimeOffset.UtcNow };
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CronExecutionRecord>> ListExecutionsAsync(string jobId, int limit = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CronExecutionRecord>>(
                _executions.Where(e => e.JobId == jobId).OrderByDescending(e => e.ScheduledFor).Take(limit).ToList());
    }
}
