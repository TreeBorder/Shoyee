using Xunit;
using System.Text.Json;
using Shoyee.Client.Rpc;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;

namespace Shoyee.Gateway.Tests.Rpc;

public sealed class GatewayRpcTests
{
    [Fact]
    public async Task Hello_ReturnsServerInfoAndLanes()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        Assert.Equal(RpcProtocol.Version, client.Hello.ServerVersion);
        Assert.Equal(bed.Root, client.Hello.Workspace);
        Assert.Contains(client.Hello.Lanes, lane => lane.Id == LaneIds.DesktopRoleplay);
        Assert.Contains(client.Hello.Lanes, lane => lane.Id == "mattermost:work");
        Assert.Contains("turn.stream", client.Hello.Capabilities);
    }

    [Fact]
    public async Task Hello_MajorVersionMismatch_IsRejected()
    {
        await using var bed = await GatewayTestBed.StartAsync();

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => bed.ConnectAsync(ClientRole.DesktopWork, version: "2.0.0"));

        Assert.Equal(RpcErrors.IncompatibleVersion, ex.Code);
    }

    [Fact]
    public async Task UnimplementedMethod_ReturnsMethodNotFound()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        // 注册表里没有的方法一律 -32601（不按前缀猜）
        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync("image.nope", new { prompt = "hi" }));

        Assert.Equal(RpcErrors.MethodNotFound, ex.Code);
    }

    [Fact]
    public async Task RoleplayConnection_CannotCallWorkSessionMethods()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.SessionList, new { }));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
    }

    [Fact]
    public async Task RoleplayLaneMessages_DefaultsToOwnLane()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        await client.CallAsync(RpcMethods.LaneMessages, new { });

        Assert.Equal(LaneIds.DesktopRoleplay, bed.Sessions.LastLaneMessagesLaneId);
    }

    [Fact]
    public async Task RoleplayLaneMessages_OtherRoleplayLane_IsAllowed()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        await client.CallAsync(RpcMethods.LaneMessages, new { laneId = "mattermost:roleplay" });

        Assert.Equal("mattermost:roleplay", bed.Sessions.LastLaneMessagesLaneId);
    }

    [Fact]
    public async Task RoleplayLaneMessages_WorkLane_IsRejected()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.LaneMessages, new { laneId = "mattermost:work" }));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
    }

    [Fact]
    public async Task RoleplayTurnSend_FillsLaneFromConnection()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var turn = await client.CallAsync<TurnRef>(RpcMethods.TurnSend, new { text = "在吗" });

        Assert.Equal("trn_test", turn.TurnId);
        var sent = Assert.Single(bed.Turns.Sent);
        Assert.Equal(LaneIds.DesktopRoleplay, sent.LaneId);
        Assert.Null(sent.SessionId);
    }

    [Fact]
    public async Task StatusGet_ReturnsLanesUsageStatesAndDb()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var status = await client.CallAsync<StatusDto>(RpcMethods.StatusGet);

        Assert.Equal(3, status.Lanes.Count);
        Assert.Equal(2, status.UsageToday.Calls);
        Assert.Equal(1024, status.Db.SizeBytes);
        Assert.Contains(status.States, state => state.Key == "mood");
        Assert.True(status.UptimeSeconds >= 0);
    }

    [Fact]
    public async Task DaemonShutdown_NotifiesHost()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.Ctl);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Control.ShutdownRequested += () => requested.TrySetResult();

        await client.CallAsync(RpcMethods.DaemonShutdown, new { });

        var completed = await Task.WhenAny(requested.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(requested.Task, completed);
    }

    [Fact]
    public async Task ShutdownGate_RejectsNewTurns()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);
        bed.Gate.MarkStopping();

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.TurnSend, new TurnSendRequest("还在吗", "ses_1")));

        Assert.Equal(RpcErrors.Busy, ex.Code);
    }

    [Fact]
    public async Task IndexUpsertThenList_RoundTrips()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);
        var now = DateTimeOffset.UtcNow;
        var entry = new IndexEntry(0, "nas-photos", "照片库", "path", "/tmp/photos", "all", null, null, false, now, now);

        var stored = await client.CallAsync<IndexEntry>(RpcMethods.IndexUpsert, new IndexUpsertRequest(entry));
        var list = await client.CallAsync<IndexListResult>(RpcMethods.IndexList, new { });

        Assert.Equal(1, stored.Id);
        Assert.Contains(list.Items, item => item.Key == "nas-photos");
    }

    [Fact]
    public async Task ConfigGet_ReturnsWorkspaceRoot()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var config = await client.CallAsync<WorkspaceConfig>(RpcMethods.ConfigGet);

        Assert.Equal(bed.Root, config.Root);
        Assert.Equal("MAIN_KEY", config.Models.Main.ApiKeyEnv);
    }

    [Fact]
    public async Task BadParams_ReturnsInvalidParams()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.SessionGet, new { }));

        Assert.Equal(RpcErrors.InvalidParams, ex.Code);
    }
}
