using Xunit;
using Shoyee.Client.Rpc;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Gateway.Tests.Rpc;

public sealed class CronRpcTests
{
    [Fact]
    public async Task CronList_ReturnsWorkJobsSortedByNextRun()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        SeedJob(bed, "cron_later", "晚一点", nextRunAt: DateTimeOffset.UtcNow.AddHours(2));
        SeedJob(bed, "cron_sooner", "马上", nextRunAt: DateTimeOffset.UtcNow.AddMinutes(5));
        SeedJob(bed, "cron_disabled", "停用了", enabled: false, nextRunAt: DateTimeOffset.UtcNow.AddDays(1));
        // RP 任务不出现在工作桌面的任务页
        SeedJob(bed, "cron_rp", "定时问候", mode: Mode.Roleplay, nextRunAt: DateTimeOffset.UtcNow);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<CronListResult>(RpcMethods.CronList, new { });

        Assert.Equal(
            ["cron_sooner", "cron_later", "cron_disabled"],
            result.Items.Select(item => item.Id).ToArray());
        Assert.DoesNotContain(result.Items, item => item.Id == "cron_rp");
    }

    [Fact]
    public async Task CronList_IncludeDisabledFalse_SkipsDisabled()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        SeedJob(bed, "cron_on", "开着");
        SeedJob(bed, "cron_off", "关了", enabled: false);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<CronListResult>(
            RpcMethods.CronList,
            new CronListRequest(IncludeDisabled: false));

        Assert.Equal("cron_on", Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task CronGet_ReturnsJobAndRecentExecutions()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        SeedJob(bed, "cron_a", "早报");
        bed.Cron.SeedExecution(new CronExecutionRecord(
            "cron_a",
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-2),
            "succeeded",
            "第一份"));
        bed.Cron.SeedExecution(new CronExecutionRecord(
            "cron_a",
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(-1),
            null,
            "running",
            null));
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var detail = await client.CallAsync<CronJobDetail>(
            RpcMethods.CronGet,
            new CronGetRequest("cron_a", Executions: 5));

        Assert.Equal("早报", detail.Job.Name);
        Assert.Equal(2, detail.Executions.Count);
        Assert.Equal("running", detail.Executions[0].State);
        Assert.Equal("第一份", detail.Executions[1].Result);
    }

    [Fact]
    public async Task CronGet_RoleplayJob_IsNotFoundForWorkDesktop()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        SeedJob(bed, "cron_rp", "定时问候", mode: Mode.Roleplay);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.CronGet, new CronGetRequest("cron_rp")));

        Assert.Equal(RpcErrors.NotFound, ex.Code);
    }

    [Fact]
    public async Task CronSetEnabled_UpdatesJobAndRefreshesScheduler()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        SeedJob(bed, "cron_a", "早报");
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        await client.CallAsync(RpcMethods.CronSetEnabled, new CronSetEnabledRequest("cron_a", false));

        var stored = await bed.Cron.GetAsync("cron_a");
        Assert.NotNull(stored);
        Assert.False(stored.Enabled);
        Assert.Equal(["cron_a"], bed.CronScheduler.Refreshed);
    }

    [Fact]
    public async Task CronRemove_DeletesJobAndRefreshesScheduler()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        SeedJob(bed, "cron_a", "早报");
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        await client.CallAsync(RpcMethods.CronRemove, new CronRemoveRequest("cron_a"));

        Assert.Null(await bed.Cron.GetAsync("cron_a"));
        Assert.Equal(["cron_a"], bed.CronScheduler.Refreshed);
    }

    [Fact]
    public async Task CronRemove_UnknownJob_IsNotFound()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.CronRemove, new CronRemoveRequest("cron_missing")));

        Assert.Equal(RpcErrors.NotFound, ex.Code);
    }

    [Fact]
    public async Task RoleplayConnection_CannotCallCronMethods()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.CronList, new { }));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
    }

    private static CronJobRecord SeedJob(
        GatewayTestBed bed,
        string id,
        string name,
        bool enabled = true,
        Mode mode = Mode.Work,
        DateTimeOffset? nextRunAt = null)
    {
        var job = new CronJobRecord(
            Id: id,
            Name: name,
            Schedule: "every:30m",
            Kind: "prompt",
            Payload: "总结一下",
            Deliver: "local",
            Enabled: enabled,
            LastRunAt: null,
            CreatedAt: DateTimeOffset.UnixEpoch,
            Mode: mode,
            NextRunAt: nextRunAt ?? DateTimeOffset.UtcNow.AddHours(1));
        bed.Cron.Seed(job);
        return job;
    }
}
