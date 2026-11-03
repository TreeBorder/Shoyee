using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Proactive.Heartbeat;
using Shoyee.Proactive.Tests.TestSupport;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Proactive.Tests.Heartbeat;

public sealed class HeartbeatServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "shoyee-heartbeat-" + Guid.NewGuid().ToString("N")[..8]);

    public HeartbeatServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Disabled_DoesNothing()
    {
        var (service, turns, delivery, states) = Create(
            lanes: [ProactiveTestData.Desktop()],
            longing: 100,
            options: Options() with { Enabled = false });

        var result = await service.ExecuteAsync();

        Assert.Equal(HeartbeatZone.Disabled, result.Zone);
        Assert.Empty(turns.Requests);
        Assert.Empty(delivery.Sent);
        Assert.Equal("100", states.Rows["longing"].Value);
    }

    [Fact]
    public async Task HotZone_DoesNotSendOrChangeLonging()
    {
        var lanes = new LaneStore(ProactiveTestData.Desktop(DateTimeOffset.UtcNow.AddMinutes(-5)));
        var (service, turns, _, states) = Create(lanes: lanes, longing: 100);

        var result = await service.ExecuteAsync();

        Assert.Equal(HeartbeatZone.Hot, result.Zone);
        Assert.Empty(turns.Requests);
        Assert.Equal("100", states.Rows["longing"].Value);
    }

    [Fact]
    public async Task DesktopOnline_SendsTurnOnDesktopLane_NoImDelivery()
    {
        var lanes = new LaneStore(
            ProactiveTestData.Desktop(DateTimeOffset.UtcNow.AddMinutes(-60)),
            ProactiveTestData.Im("mattermost:roleplay", DateTimeOffset.UtcNow.AddMinutes(-60), "mm-target"));
        var presence = new FakePresence();
        presence.Online.Add(ClientRole.DesktopRoleplay);

        var (service, turns, delivery, states) = Create(lanes: lanes, longing: 100, presence: presence);

        var result = await service.ExecuteAsync();

        Assert.True(result.Triggered);
        Assert.Equal(LaneIds.DesktopRoleplay, result.LaneId);
        Assert.False(result.Delivered);
        Assert.Single(turns.Requests);
        Assert.Equal(LaneIds.DesktopRoleplay, turns.Requests[0].LaneId);
        Assert.True(turns.Requests[0].SystemInput); // 注入内容不是用户可见消息
        Assert.Contains("系统消息", turns.Requests[0].Text, StringComparison.Ordinal);
        Assert.Empty(delivery.Sent);
        Assert.Equal("50", states.Rows["longing"].Value); // 100 - 惩罚 50
    }

    [Fact]
    public async Task DesktopOffline_SendsTurnOnImLane_AndDeliversText()
    {
        var lanes = new LaneStore(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", DateTimeOffset.UtcNow.AddMinutes(-60), "mm-target"));

        var (service, turns, delivery, _) = Create(lanes: lanes, longing: 100);

        var result = await service.ExecuteAsync();

        Assert.True(result.Triggered);
        Assert.True(result.Delivered);
        Assert.Equal("mattermost:roleplay", result.LaneId);
        Assert.Single(turns.Requests);
        var sent = Assert.Single(delivery.Sent);
        Assert.Equal("mattermost:roleplay", sent.LaneId);
        Assert.Equal("mm-target", sent.Target);
        Assert.Equal("我在呢", sent.Text);
        Assert.Equal("heartbeat", sent.Kind);
    }

    [Fact]
    public async Task OutboundAttachment_IsDeliveredAfterHeartbeatText()
    {
        var lanes = new LaneStore(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", DateTimeOffset.UtcNow.AddMinutes(-60), "mm-target"));

        var (service, turns, delivery, _) = Create(lanes: lanes, longing: 100);
        turns.Outcome = new TurnOutcome(
            "trn_1",
            true,
            "给你录了一段",
            null,
            [new MessageAttachment("/tmp/hello.wav", "hello.wav", "audio/wav", 10, AttachmentKind.Audio)]);

        var result = await service.ExecuteAsync();

        Assert.True(result.Triggered);
        Assert.Equal("给你录了一段", Assert.Single(delivery.Sent).Text);
        var file = Assert.Single(delivery.Files);
        Assert.Equal("/tmp/hello.wav", file.FilePath);
        Assert.Equal("heartbeat", file.Kind);
    }

    [Fact]
    public async Task AttachmentOnlyTurn_IsStillDelivered()
    {
        var lanes = new LaneStore(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", DateTimeOffset.UtcNow.AddMinutes(-60), "mm-target"));

        var (service, turns, delivery, _) = Create(lanes: lanes, longing: 100);
        turns.Outcome = new TurnOutcome(
            "trn_1",
            true,
            string.Empty,
            null,
            [new MessageAttachment("/tmp/hello.wav", "hello.wav", "audio/wav", 10, AttachmentKind.Audio)]);

        var result = await service.ExecuteAsync();

        Assert.True(result.Triggered);
        Assert.Empty(delivery.Sent);
        Assert.Single(delivery.Files);
    }

    [Fact]
    public async Task NoUsableTarget_DoesNotRunTurn()
    {
        var lanes = new LaneStore(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", DateTimeOffset.UtcNow.AddMinutes(-60), null));

        var (service, turns, delivery, _) = Create(lanes: lanes, longing: 100);

        var result = await service.ExecuteAsync();

        Assert.False(result.Triggered);
        Assert.Empty(turns.Requests);
        Assert.Empty(delivery.Sent);
    }

    [Fact]
    public async Task FailedTurn_IsNotDelivered()
    {
        var lanes = new LaneStore(
            ProactiveTestData.Desktop(),
            ProactiveTestData.Im("mattermost:roleplay", DateTimeOffset.UtcNow.AddMinutes(-60), "mm-target"));

        var (service, turns, delivery, _) = Create(
            lanes: lanes,
            longing: 100,
            outcome: new TurnOutcome("trn_1", false, "半截"));

        var result = await service.ExecuteAsync();

        Assert.False(result.Triggered);
        Assert.Single(turns.Requests);
        Assert.Empty(delivery.Sent);
    }

    [Fact]
    public async Task WarmZone_IncreasesLonging_WithoutSending()
    {
        var lanes = new LaneStore(ProactiveTestData.Desktop(DateTimeOffset.UtcNow.AddMinutes(-25)));
        var options = Options() with { NormalIncreaseMin = 1, NormalIncreaseMax = 1 };
        var (service, turns, _, states) = Create(lanes: lanes, longing: 50, options: options);

        var result = await service.ExecuteAsync();

        Assert.Equal(HeartbeatZone.Warm, result.Zone);
        Assert.Empty(turns.Requests);
        Assert.Equal("51", states.Rows["longing"].Value);
    }

    [Fact]
    public async Task MissingLongingState_IsCreated()
    {
        var (service, _, _, states) = Create(
            lanes: [ProactiveTestData.Desktop(DateTimeOffset.UtcNow.AddMinutes(-25))],
            longing: null,
            options: Options() with { NormalIncreaseMin = 1, NormalIncreaseMax = 1 });

        await service.ExecuteAsync();

        Assert.True(states.Rows.ContainsKey("longing"));
        Assert.Equal("1", states.Rows["longing"].Value);
    }

    [Theory]
    [InlineData("0.2", 20)]
    [InlineData("100", 100)]
    [InlineData("1.0", 100)]
    [InlineData("abc", 0)]
    [InlineData(null, 0)]
    public void ParseLonging_HandlesLegacyAndCurrentFormats(string? value, int expected)
    {
        Assert.Equal(expected, HeartbeatService.ParseLonging(value));
    }

    private (HeartbeatService Service, RecordingTurnService Turns, RecordingDelivery Delivery, FakeStateRepository States)
        Create(
            LaneRecord[] lanes,
            int? longing,
            HeartbeatOptions? options = null,
            FakePresence? presence = null,
            TurnOutcome? outcome = null)
        => Create(new LaneStore(lanes), longing, options, presence, outcome);

    private (HeartbeatService Service, RecordingTurnService Turns, RecordingDelivery Delivery, FakeStateRepository States)
        Create(
            LaneStore lanes,
            int? longing,
            HeartbeatOptions? options = null,
            FakePresence? presence = null,
            TurnOutcome? outcome = null)
    {
        var states = new FakeStateRepository();
        if (longing is not null)
        {
            states.Rows["longing"] = new StateRecord(
                "longing", "思念", longing.Value.ToString(), null, false, DateTimeOffset.UtcNow);
        }

        var presenceValue = presence ?? new FakePresence();
        var turns = new RecordingTurnService { Outcome = outcome ?? new TurnOutcome("trn_1", true, "我在呢") };
        var delivery = new RecordingDelivery();
        var service = new HeartbeatService(
            lanes.Repository,
            states,
            new HeartbeatTargetSelector(lanes.Repository, presenceValue),
            turns,
            delivery,
            new HeartbeatPromptBook(_root),
            ProactiveTestData.Config(_root, options ?? Options()),
            TimeProvider.System,
            _ => 0);

        return (service, turns, delivery, states);
    }

    private static HeartbeatOptions Options()
        => new(
            Enabled: true,
            MinMinutes: 45,
            MaxMinutes: 75,
            CooldownMinutes: 30,
            HotZoneMinutes: 20,
            LongingCap: 100,
            NormalIncreaseMin: 0,
            NormalIncreaseMax: 0,
            TriggerPenalty: 50,
            LevelFluctuationChance: 0,
            QuietHoursStart: new TimeOnly(0, 0),
            QuietHoursEnd: new TimeOnly(0, 0));

    /// <summary>包装 <see cref="FakeLaneRepository"/>，以便用例里直接写 Lane 数组。</summary>
    private sealed class LaneStore
    {
        public LaneStore(params LaneRecord[] lanes)
        {
            Repository = new FakeLaneRepository(lanes);
        }

        public FakeLaneRepository Repository { get; }
    }
}
