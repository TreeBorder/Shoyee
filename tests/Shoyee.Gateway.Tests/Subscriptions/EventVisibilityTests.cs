using Xunit;
using System.Collections.Concurrent;
using System.Text.Json;
using Shoyee.Client.Rpc;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;

namespace Shoyee.Gateway.Tests.Subscriptions;

public sealed class EventVisibilityTests
{
    [Fact]
    public async Task RoleplayConnection_ReceivesOnlyItsOwnLaneEvents()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var roleplay = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);
        await using var work = await bed.ConnectAsync(ClientRole.DesktopWork);
        await using var menu = await bed.ConnectAsync(ClientRole.Menu);

        var roleplayEvents = Record(roleplay);
        var workEvents = Record(work);
        var menuEvents = Record(menu);

        await bed.Hub.PublishAsync(
            RpcEvents.TurnDelta,
            new TurnDeltaEvent("trn_1", LaneIds.DesktopRoleplay, "在", LaneIds.DesktopRoleplay));
        await Task.Delay(300);

        Assert.Contains(roleplayEvents, name => name == RpcEvents.TurnDelta);
        Assert.DoesNotContain(RpcEvents.TurnDelta, workEvents);
        Assert.DoesNotContain(RpcEvents.TurnDelta, menuEvents);
    }

    [Fact]
    public async Task RoleplayConnection_ReceivesOtherRoleplayLaneEvents()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var roleplay = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);
        await using var work = await bed.ConnectAsync(ClientRole.DesktopWork);

        var roleplayEvents = Record(roleplay);
        var workEvents = Record(work);

        await bed.Hub.PublishAsync(
            RpcEvents.TurnDelta,
            new TurnDeltaEvent("trn_rp", "mattermost:roleplay", "在", "mattermost:roleplay"));
        await Task.Delay(300);

        Assert.Contains(roleplayEvents, name => name == RpcEvents.TurnDelta);
        Assert.DoesNotContain(RpcEvents.TurnDelta, workEvents);
    }

    [Fact]
    public async Task RoleplayConnection_ReceivesGlobalStateEvents()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var roleplay = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);
        await using var work = await bed.ConnectAsync(ClientRole.DesktopWork);

        var roleplayEvents = Record(roleplay);
        var workEvents = Record(work);

        await bed.Hub.PublishAsync(
            RpcEvents.StateUpdated,
            new StateDto("mood", "心情", "愉快", null, true, DateTimeOffset.UtcNow));
        await Task.Delay(300);

        Assert.Contains(roleplayEvents, name => name == RpcEvents.StateUpdated);
        Assert.Contains(workEvents, name => name == RpcEvents.StateUpdated);
    }

    [Fact]
    public async Task WorkConnection_ReceivesWorkEventsButNotRoleplayEvents()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var roleplay = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);
        await using var work = await bed.ConnectAsync(ClientRole.DesktopWork);

        var roleplayEvents = Record(roleplay);
        var workEvents = Record(work);

        await bed.Hub.PublishAsync(
            RpcEvents.TurnDelta,
            new TurnDeltaEvent("trn_2", "mattermost:work", "周报", "mattermost:work"));
        await Task.Delay(300);

        Assert.Contains(workEvents, name => name == RpcEvents.TurnDelta);
        Assert.DoesNotContain(RpcEvents.TurnDelta, roleplayEvents);
    }

    [Fact]
    public async Task MenuConnection_ReceivesOnlyStatusEvents()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var menu = await bed.ConnectAsync(ClientRole.Menu);

        var menuEvents = Record(menu);

        await bed.Hub.PublishAsync(RpcEvents.DaemonStatus, new { uptime = 10 });
        await bed.Hub.PublishAsync(
            RpcEvents.TurnDelta,
            new TurnDeltaEvent("trn_3", "mattermost:work", "x", "mattermost:work"));
        await Task.Delay(300);

        Assert.Contains(menuEvents, name => name == RpcEvents.DaemonStatus);
        Assert.DoesNotContain(RpcEvents.TurnDelta, menuEvents);
    }

    [Fact]
    public async Task ApprovalEvents_FollowLaneVisibility()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var roleplay = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);
        await using var work = await bed.ConnectAsync(ClientRole.DesktopWork);

        var roleplayEvents = Record(roleplay);
        var workEvents = Record(work);
        var now = DateTimeOffset.UtcNow;
        await bed.Hub.PublishAsync(
            RpcEvents.ApprovalRequest,
            new ApprovalRequestDto(
                "apv_1",
                LaneIds.DesktopRoleplay,
                "trn_1",
                "shell_run",
                "ls",
                null,
                now,
                now.AddMinutes(10),
                LaneIds.DesktopRoleplay));
        await Task.Delay(300);

        Assert.Contains(roleplayEvents, name => name == RpcEvents.ApprovalRequest);
        Assert.DoesNotContain(RpcEvents.ApprovalRequest, workEvents);
    }

    [Fact]
    public async Task Menu_ReceivesPeriodicDaemonStatus()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var menu = await bed.ConnectAsync(ClientRole.Menu);

        var status = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        menu.EventReceived += (name, payload) =>
        {
            if (name == RpcEvents.DaemonStatus)
            {
                status.TrySetResult(payload);
            }
        };

        var completed = await Task.WhenAny(status.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(status.Task, completed);
        var payload = await status.Task;
        Assert.Equal(RpcProtocol.Version, payload.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Presence_TracksConnectedClientsByRole()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        Assert.False(bed.Hub.IsOnline(ClientRole.DesktopRoleplay));
        Assert.False(bed.Hub.IsOnline(ClientRole.Menu));

        var roleplay = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);
        var menu = await bed.ConnectAsync(ClientRole.Menu);

        Assert.True(bed.Hub.IsOnline(ClientRole.DesktopRoleplay));
        Assert.True(bed.Hub.IsOnline(ClientRole.Menu));
        Assert.False(bed.Hub.IsOnline(ClientRole.DesktopWork));

        await roleplay.DisposeAsync();
        await WaitUntilAsync(() => !bed.Hub.IsOnline(ClientRole.DesktopRoleplay));
        Assert.True(bed.Hub.IsOnline(ClientRole.Menu));

        await menu.DisposeAsync();
        await WaitUntilAsync(() => !bed.Hub.IsOnline(ClientRole.Menu));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("等待连接状态变化超时");
            }

            await Task.Delay(20);
        }
    }

    private static ConcurrentQueue<string> Record(RpcClient client)
    {
        var events = new ConcurrentQueue<string>();
        client.EventReceived += (name, _) => events.Enqueue(name);
        return events;
    }
}
