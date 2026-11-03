using Shoyee.Contracts.Domain;
using Shoyee.Proactive.Heartbeat;
using Shoyee.Proactive.Tests.TestSupport;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Proactive.Tests.Heartbeat;

public sealed class HeartbeatTargetSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DesktopOnline_AlwaysWins_EvenWhenImLaneIsMoreRecent()
    {
        var lanes = new FakeLaneRepository(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", Now, "mm-target"),
            ProactiveTestData.Im("telegram:roleplay", Now.AddMinutes(5), "tg-target"));

        var target = await new HeartbeatTargetSelector(lanes, Presence(ClientRole.DesktopRoleplay))
            .SelectAsync();

        Assert.NotNull(target);
        Assert.Equal("desktop:roleplay", target.Id);
    }

    [Fact]
    public async Task DesktopOffline_FallsBackToMostRecentlyActiveImLaneWithTarget()
    {
        var lanes = new FakeLaneRepository(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", Now.AddMinutes(-30), "mm-target"),
            ProactiveTestData.Im("mattermost:other", Now.AddMinutes(-5), "newer-target"));

        var target = await new HeartbeatTargetSelector(lanes, Presence())
            .SelectAsync();

        Assert.NotNull(target);
        Assert.Equal("mattermost:other", target.Id);
    }

    [Fact]
    public async Task ImLaneWithoutTargetIsSkipped()
    {
        var lanes = new FakeLaneRepository(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", Now, null),
            ProactiveTestData.Im("mattermost:other", Now.AddHours(-2), "target"));

        var target = await new HeartbeatTargetSelector(lanes, Presence())
            .SelectAsync();

        Assert.NotNull(target);
        Assert.Equal("mattermost:other", target.Id);
    }

    [Fact]
    public async Task WorkLane_IsNeverAHeartbeatTarget()
    {
        var lanes = new FakeLaneRepository(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", Now.AddDays(-1), "rp-target"),
            new LaneRecord(
                "mattermost:work",
                LaneChannel.Mattermost,
                Mode.Work,
                "http://mm",
                "工作",
                null,
                Now,
                Now,
                Now,
                "work-target"));

        var target = await new HeartbeatTargetSelector(lanes, Presence())
            .SelectAsync();

        Assert.NotNull(target);
        Assert.Equal("mattermost:roleplay", target.Id);
    }

    [Fact]
    public async Task DesktopOffline_AndNoUsableImLane_ReturnsNull()
    {
        var lanes = new FakeLaneRepository(ProactiveTestData.Desktop());

        var target = await new HeartbeatTargetSelector(lanes, Presence())
            .SelectAsync();

        Assert.Null(target);
    }

    [Fact]
    public async Task DesktopOnline_ButDesktopLaneMissing_StillFallsBackToIm()
    {
        var lanes = new FakeLaneRepository(
            ProactiveTestData.Im("mattermost:roleplay", Now, "mm-target"));

        var target = await new HeartbeatTargetSelector(lanes, Presence(ClientRole.DesktopRoleplay))
            .SelectAsync();

        Assert.NotNull(target);
        Assert.Equal("mattermost:roleplay", target.Id);
    }

    private static FakePresence Presence(params ClientRole[] roles)
    {
        var presence = new FakePresence();
        foreach (var role in roles)
        {
            presence.Online.Add(role);
        }

        return presence;
    }
}
