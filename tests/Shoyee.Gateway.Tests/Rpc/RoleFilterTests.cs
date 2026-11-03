using Xunit;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Rpc;

namespace Shoyee.Gateway.Tests.Rpc;

public sealed class RoleFilterTests
{
    [Theory]
    [InlineData(RpcMethods.Hello, true)]
    [InlineData(RpcMethods.Ping, true)]
    [InlineData(RpcMethods.StatusGet, false)]
    public void UnverifiedConnection_CanOnlyHandshake(string method, bool expected)
        => Assert.Equal(expected, RoleFilter.CanCall(null, method));

    [Theory]
    [InlineData(RpcMethods.LaneMessages, true)]
    [InlineData(RpcMethods.TurnSend, true)]
    [InlineData(RpcMethods.ApprovalRespond, true)]
    [InlineData(RpcMethods.SessionSearch, true)]
    [InlineData(RpcMethods.SessionList, false)]
    [InlineData(RpcMethods.SessionCreate, false)]
    [InlineData(RpcMethods.DaemonShutdown, false)]
    [InlineData(RpcMethods.ToolsList, false)]
    [InlineData(RpcMethods.McpStatus, false)]
    [InlineData(RpcMethods.StateSet, false)]
    [InlineData(RpcMethods.UsageGet, false)]
    public void RoleplayDesktop_IsLimitedToItsLaneAndTurns(string method, bool expected)
        => Assert.Equal(expected, RoleFilter.CanCall(ClientRole.DesktopRoleplay, method));

    [Theory]
    [InlineData(RpcMethods.SessionCreate, true)]
    [InlineData(RpcMethods.SessionSearch, true)]
    [InlineData(RpcMethods.ConfigGet, true)]
    [InlineData(RpcMethods.IndexList, true)]
    [InlineData(RpcMethods.ToolsList, true)]
    [InlineData(RpcMethods.McpStatus, true)]
    [InlineData(RpcMethods.StateSet, true)]
    [InlineData(RpcMethods.UsageGet, true)]
    [InlineData(RpcMethods.DaemonRestart, true)]
    [InlineData(RpcMethods.DaemonShutdown, false)]
    public void WorkDesktop_GetsWorkAndIndexMethods(string method, bool expected)
        => Assert.Equal(expected, RoleFilter.CanCall(ClientRole.DesktopWork, method));

    [Theory]
    [InlineData(RpcMethods.DaemonStatus, true)]
    [InlineData(RpcMethods.StatusGet, true)]
    [InlineData(RpcMethods.SessionList, false)]
    [InlineData(RpcMethods.TurnSend, false)]
    [InlineData(RpcMethods.ToolsList, false)]
    [InlineData(RpcMethods.McpReload, false)]
    public void Menu_IsStatusOnly(string method, bool expected)
        => Assert.Equal(expected, RoleFilter.CanCall(ClientRole.Menu, method));

    [Theory]
    [InlineData(RpcMethods.DaemonStatus, true)]
    [InlineData(RpcMethods.DaemonShutdown, true)]
    [InlineData(RpcMethods.ConfigGet, true)]
    [InlineData(RpcMethods.McpStatus, true)]
    [InlineData(RpcMethods.DaemonRestart, true)]
    [InlineData(RpcMethods.StateSet, true)]
    [InlineData(RpcMethods.SessionList, false)]
    public void Ctl_GetsStatusAndMaintenance(string method, bool expected)
        => Assert.Equal(expected, RoleFilter.CanCall(ClientRole.Ctl, method));

    [Fact]
    public void RoleplayConnection_ReceivesAllRoleplayLanes()
    {
        Assert.True(RoleFilter.CanReceive(
            ClientRole.DesktopRoleplay,
            "desktop:roleplay",
            RpcEvents.TurnDelta,
            "desktop:roleplay",
            fromRoleplayLane: true));

        // 桌面 IM 视图可以切到其他 RP 渠道，事件也一并放行
        Assert.True(RoleFilter.CanReceive(
            ClientRole.DesktopRoleplay,
            "desktop:roleplay",
            RpcEvents.TurnDelta,
            "mattermost:roleplay",
            fromRoleplayLane: true));

        Assert.False(RoleFilter.CanReceive(
            ClientRole.DesktopRoleplay,
            "desktop:roleplay",
            RpcEvents.TurnDelta,
            "mattermost:work",
            fromRoleplayLane: false));

        Assert.False(RoleFilter.CanReceive(
            ClientRole.DesktopRoleplay,
            "desktop:roleplay",
            RpcEvents.DaemonStatus,
            eventLaneId: null,
            fromRoleplayLane: false));
    }

    [Fact]
    public void WorkConnection_SkipsRoleplayEvents()
    {
        Assert.False(RoleFilter.CanReceive(
            ClientRole.DesktopWork,
            null,
            RpcEvents.TurnDelta,
            "desktop:roleplay",
            fromRoleplayLane: true));

        Assert.True(RoleFilter.CanReceive(
            ClientRole.DesktopWork,
            null,
            RpcEvents.TurnDelta,
            "mattermost:work",
            fromRoleplayLane: false));

        Assert.True(RoleFilter.CanReceive(
            ClientRole.DesktopWork,
            null,
            RpcEvents.LaneStatus,
            eventLaneId: null,
            fromRoleplayLane: false));
    }

    [Fact]
    public void Menu_ReceivesOnlyStatusEvents()
    {
        Assert.True(RoleFilter.CanReceive(
            ClientRole.Menu, null, RpcEvents.DaemonStatus, eventLaneId: null, fromRoleplayLane: false));
        Assert.False(RoleFilter.CanReceive(
            ClientRole.Menu, null, RpcEvents.TurnDelta, "mattermost:work", fromRoleplayLane: false));
    }
}
