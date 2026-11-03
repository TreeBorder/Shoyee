using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Channels.Core;
using Shoyee.Channels.Delivery;
using Shoyee.Channels.Tests.TestSupport;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Handlers;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Channels.Tests.Core;

/// <summary>入站管线：Lane 模式分派、IM 会话指针、去重、失败提示。</summary>
public sealed class InboundPipelineTests
{
    private const string WorkLane = "mattermost:work";
    private const string RoleplayLane = "mattermost:roleplay";

    [Fact]
    public async Task Roleplay_SendsByLane_WithoutSession()
    {
        var (pipeline, turns, _, lanes, _) = Create();
        lanes.Seed(Lane(RoleplayLane, Mode.Roleplay));

        await pipeline.OnInboundAsync(Message(RoleplayLane));

        var request = Assert.Single(turns.Requests);
        Assert.Equal(RoleplayLane, request.LaneId);
        Assert.Null(request.SessionId);
        Assert.Equal("你好", request.Text);
        Assert.Equal("mm:channel:c1:root", request.ReplyTarget);
    }

    [Fact]
    public async Task Work_WithoutActiveSession_CreatesAndBindsOne()
    {
        var (pipeline, turns, sessions, lanes, _) = Create();
        lanes.Seed(Lane(WorkLane, Mode.Work));

        await pipeline.OnInboundAsync(Message(WorkLane));

        var created = Assert.Single(sessions.Created);
        Assert.Equal(Mode.Work, created.Mode);
        Assert.Equal(WorkLane, created.LaneId);
        Assert.Equal(("ses_1", WorkLane), Assert.Single(sessions.Activated));
        Assert.Equal("ses_1", Assert.Single(turns.Requests).SessionId);
    }

    [Fact]
    public async Task Work_WithActiveSession_ReusesIt()
    {
        var (pipeline, turns, sessions, lanes, _) = Create();
        lanes.Seed(Lane(WorkLane, Mode.Work) with { ActiveSessionId = "ses_existing" });

        await pipeline.OnInboundAsync(Message(WorkLane));

        Assert.Empty(sessions.Created);
        Assert.Equal("ses_existing", Assert.Single(turns.Requests).SessionId);
    }

    [Fact]
    public async Task DuplicateExternalId_IsIgnored()
    {
        var (pipeline, turns, _, lanes, _) = Create();
        lanes.Seed(Lane(WorkLane, Mode.Work));

        var first = Message(WorkLane);
        var duplicate = first with { ExternalId = first.ExternalId };

        await pipeline.OnInboundAsync(first);
        await pipeline.OnInboundAsync(duplicate);

        Assert.Single(turns.Requests);
    }

    [Fact]
    public async Task UnknownLane_IsIgnored()
    {
        var (pipeline, turns, _, _, senders) = Create();

        await pipeline.OnInboundAsync(Message("mattermost:unknown"));

        Assert.Empty(turns.Requests);
        Assert.Empty(senders[WorkLane].Texts);
    }

    [Fact]
    public async Task TouchesInboundTimestamp()
    {
        var (pipeline, _, _, lanes, _) = Create();
        lanes.Seed(Lane(RoleplayLane, Mode.Roleplay));
        var at = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        await pipeline.OnInboundAsync(Message(RoleplayLane, receivedAt: at));

        Assert.Equal((RoleplayLane, at), Assert.Single(lanes.Touched));
    }

    [Fact]
    public async Task Inbound_StoresTargetForProactiveDelivery()
    {
        var (pipeline, _, _, lanes, _) = Create();
        lanes.Seed(Lane(RoleplayLane, Mode.Roleplay));

        await pipeline.OnInboundAsync(Message(RoleplayLane));

        var lane = await lanes.GetAsync(RoleplayLane);
        Assert.Equal("mm:channel:c1:root", lane!.LastTarget);
    }

    [Fact]
    public async Task TurnFailure_NotifiesThroughLedger()
    {
        var error = new RpcException(RpcErrors.Busy, "该会话还有待审批的工具调用，先处理审批再发消息");
        var (pipeline, _, _, lanes, senders) = Create(turnFailure: error);
        lanes.Seed(Lane(WorkLane, Mode.Work) with { ActiveSessionId = "ses_1" });

        await pipeline.OnInboundAsync(Message(WorkLane));

        var (target, text) = Assert.Single(senders[WorkLane].Texts);
        Assert.Equal("mm:channel:c1:root", target);
        Assert.Contains("没跑起来", text);
        Assert.Contains("审批", text);
    }

    [Fact]
    public async Task Attachments_AreStructured_NotPastedIntoText()
    {
        var (pipeline, turns, _, lanes, _) = Create();
        lanes.Seed(Lane(RoleplayLane, Mode.Roleplay));
        var message = Message(RoleplayLane) with
        {
            Attachments = [new InboundAttachment("a.png", "image/png", "/tmp/inbox/a.png", 10, InboundAttachmentKind.Image)],
        };

        await pipeline.OnInboundAsync(message);

        var request = Assert.Single(turns.Requests);
        Assert.Equal("你好", request.Text); // 正文保持干净，路径不再拼进来
        var attachment = Assert.Single(request.Attachments!);
        Assert.Equal("/tmp/inbox/a.png", attachment.Path);
        Assert.Equal("a.png", attachment.FileName);
        Assert.Equal("image/png", attachment.Mime);
        Assert.Equal(AttachmentKind.Image, attachment.Kind);
        Assert.Equal(10, attachment.SizeBytes);
    }

    [Fact]
    public async Task KnownCommand_IsInterceptedBeforeTurn()
    {
        var (pipeline, turns, sessions, lanes, senders) = Create();
        lanes.Seed(Lane(WorkLane, Mode.Work) with { ActiveSessionId = "ses_1" });

        await pipeline.OnInboundAsync(Message(WorkLane, text: "/echo 你好"));

        // 不进模型、不建会话；结果直接回给这条消息的来源
        Assert.Empty(turns.Requests);
        Assert.Empty(sessions.Created);
        Assert.Equal(("mm:channel:c1:root", "echo:你好"), Assert.Single(senders[WorkLane].Texts));
    }

    [Fact]
    public async Task UnknownSlashCommand_PassesThroughAsMessage()
    {
        var (pipeline, turns, _, lanes, senders) = Create();
        lanes.Seed(Lane(RoleplayLane, Mode.Roleplay));

        await pipeline.OnInboundAsync(Message(RoleplayLane, text: "/volumes 里有什么"));

        Assert.Equal("/volumes 里有什么", Assert.Single(turns.Requests).Text);
        Assert.Empty(senders[RoleplayLane].Texts);
    }

    private static (InboundPipeline Pipeline, StubTurnService Turns, StubSessionService Sessions,
        InMemoryLaneRepository Lanes, Dictionary<string, RecordingSender> Senders) Create(
        Exception? turnFailure = null)
    {
        var lanes = new InMemoryLaneRepository();
        var turns = new StubTurnService();
        if (turnFailure is not null)
        {
            turns = new StubTurnService { Failure = turnFailure };
        }

        var sessions = new StubSessionService();
        var ledger = new InMemoryDeliveryRepository();
        var workSender = new RecordingSender(WorkLane);
        var roleplaySender = new RecordingSender(RoleplayLane);
        var delivery = new ReliableDelivery(
            [workSender, roleplaySender],
            ledger,
            clock: new FixedClock(DateTimeOffset.UnixEpoch));

        var pipeline = new InboundPipeline(
            lanes,
            sessions,
            turns,
            delivery,
            new CommandRouter([new EchoCommand()]),
            [],
            new InboundDeduper(),
            NullLogger<InboundPipeline>.Instance);

        return (pipeline, turns, sessions, lanes, new Dictionary<string, RecordingSender>
        {
            [WorkLane] = workSender,
            [RoleplayLane] = roleplaySender,
        });
    }

    private static LaneRecord Lane(string id, Mode mode) => new(
        id,
        LaneChannel.Mattermost,
        mode,
        "http://mm",
        "测试",
        ActiveSessionId: null,
        LastInboundAt: null,
        LastInteractionAt: null,
        DateTimeOffset.UnixEpoch);

    private static int _externalId;

    private static InboundMessage Message(string laneId, DateTimeOffset? receivedAt = null, string text = "你好") => new(
        laneId,
        "post_" + Interlocked.Increment(ref _externalId),
        "mm:channel:c1:root",
        "user_1",
        "老板",
        text,
        AddressedToBot: true,
        receivedAt ?? DateTimeOffset.UnixEpoch);
}
