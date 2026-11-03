using Xunit;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Options;
using Shoyee.Daemon.Lanes;
using Shoyee.Daemon.Tests.TestSupport;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Daemon.Tests.Lanes;

public sealed class LaneSeederTests
{
    [Fact]
    public void Build_IncludesDesktopRoleplayAndConfiguredAccounts()
    {
        var config = Config(new ChannelAccounts(
            new MattermostAccounts(
                new MattermostAccount("http://mm", "RP_TOKEN", "角色"),
                new MattermostAccount("http://mm", "WORK_TOKEN", "工作")),
            new WeChatAccount("WECHAT_TOKEN", "微信")));

        var lanes = LaneSeeder.Build(config, DateTimeOffset.UnixEpoch);

        Assert.Contains(lanes, lane => lane.Id == LaneIds.DesktopRoleplay && lane.Mode == Mode.Roleplay);
        Assert.Contains(lanes, lane => lane.Id == "mattermost:roleplay" && lane.Mode == Mode.Roleplay);
        Assert.Contains(lanes, lane => lane.Id == "mattermost:work" && lane.Mode == Mode.Work);
        Assert.Contains(lanes, lane => lane.Id == "wechat:work" && lane.Channel == LaneChannel.WeChat);
    }

    [Fact]
    public void Build_OnlyDesktop_WhenNoChannelsConfigured()
    {
        var lanes = LaneSeeder.Build(Config(new ChannelAccounts()), DateTimeOffset.UnixEpoch);

        var lane = Assert.Single(lanes);
        Assert.Equal(LaneIds.DesktopRoleplay, lane.Id);
    }

    [Fact]
    public async Task EnsureLanesAsync_PreservesRuntimeFields()
    {
        var lanes = new InMemoryLaneRepository();
        var lastInbound = DateTimeOffset.UtcNow.AddHours(-2);
        await lanes.UpsertAsync(new LaneRecord(
            LaneIds.DesktopRoleplay,
            LaneChannel.Desktop,
            Mode.Roleplay,
            "old-key",
            "旧名字",
            "ses_keep",
            lastInbound,
            lastInbound,
            DateTimeOffset.UnixEpoch));

        var ensured = await LaneSeeder.EnsureLanesAsync(
            lanes,
            Config(new ChannelAccounts()),
            TimeProvider.System);

        var lane = Assert.Single(ensured);
        Assert.Equal("ses_keep", lane.ActiveSessionId);
        Assert.Equal(lastInbound, lane.LastInboundAt);
        Assert.Equal(lastInbound, lane.LastInteractionAt);
        Assert.Equal(DateTimeOffset.UnixEpoch, lane.CreatedAt);
        Assert.Equal("desktop", lane.ExternalKey);
        Assert.Equal("桌面", lane.DisplayName);
    }

    [Fact]
    public async Task EnsureLanesAsync_DoesNotDeleteRemovedLanes()
    {
        var lanes = new InMemoryLaneRepository();
        await lanes.UpsertAsync(new LaneRecord(
            "mattermost:work",
            LaneChannel.Mattermost,
            Mode.Work,
            "http://old",
            "旧工作",
            "ses_old",
            null,
            null,
            DateTimeOffset.UnixEpoch));

        await LaneSeeder.EnsureLanesAsync(lanes, Config(new ChannelAccounts()), TimeProvider.System);

        Assert.Contains(lanes.All, lane => lane.Id == "mattermost:work");
        Assert.Contains(lanes.All, lane => lane.Id == LaneIds.DesktopRoleplay);
    }

    private static WorkspaceConfig Config(ChannelAccounts channels) => new(
        "/tmp/workspace",
        new ModelProfiles(
            new ModelEndpoint("http://127.0.0.1:1/v1", "main", "MAIN_KEY"),
            new ModelEndpoint("http://127.0.0.1:1/v1", "small", "SMALL_KEY")),
        channels,
        new ProactiveOptions(new HeartbeatOptions()),
        new ToolOptions());
}
