using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Channels.Core;
using Shoyee.Channels.Delivery;
using Shoyee.Channels.Tests.TestSupport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Xunit;

namespace Shoyee.Channels.Tests.Core;

/// <summary>回帖旁路：整轮累积、轮末一次发出、失败给提示、去向随事件携带。</summary>
public sealed class ChannelReplyRouterTests
{
    private const string Lane = "mattermost:work";
    private const string Session = "ses_1";
    private const string Target = "mm:channel:c1:root";

    [Fact]
    public async Task Completed_SendsAccumulatedTextToOriginTarget()
    {
        var (router, sender, ledger) = Create();

        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", Session, "你"));
        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", Session, "好"));
        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_1"));

        Assert.Equal((Target, "你好"), Assert.Single(sender.Texts));
        Assert.Equal(Session, Assert.Single(ledger.Rows).SessionId);
    }

    [Fact]
    public async Task Completed_StripsEveryInnerVoiceBlock()
    {
        var (router, sender, _) = Create();

        // 标签可能被切在相邻 delta 里，最终按整轮正文清洗
        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_iv", Session, "前半<InnerVoice>"));
        await router.PublishAsync(
            RpcEvents.TurnDelta,
            new TurnDeltaEvent("trn_iv", Session, "自检一</InnerVoice>中间<InnerVoice>自检二</InnerVoice>后半"));
        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_iv"));

        Assert.Equal("前半中间后半", Assert.Single(sender.Texts).Text);
    }

    [Fact]
    public async Task Completed_SendsOnlyOnce()
    {
        var (router, sender, _) = Create();

        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", Session, "在"));
        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_1"));
        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_1"));

        Assert.Single(sender.Texts);
    }

    [Fact]
    public async Task Completed_WithoutReplyTarget_SendsNothing()
    {
        var (router, sender, _) = Create();

        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_desktop", "ses_desktop", "在"));
        await router.PublishAsync(
            RpcEvents.TurnCompleted,
            Completed("trn_desktop", sessionId: "ses_desktop", replyTarget: null));

        Assert.Empty(sender.Texts);
    }

    [Fact]
    public async Task ProactiveTurn_IsNeverMirroredToIm()
    {
        var (router, sender, _) = Create();

        // 主动轮次（心跳/cron）没有回投地址：即使和用户轮次落在同一条 Lane 上，也不能被误回帖
        await router.PublishAsync(
            RpcEvents.TurnDelta,
            new TurnDeltaEvent("trn_heartbeat", Lane, "想你了", Lane));
        await router.PublishAsync(
            RpcEvents.TurnCompleted,
            Completed("trn_heartbeat", sessionId: Lane, replyTarget: null));

        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_user", Session, "在吗"));
        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_user"));

        var sent = Assert.Single(sender.Texts);
        Assert.Equal("在吗", sent.Text);
    }

    [Fact]
    public async Task Completed_SendsOutboundAttachmentsAfterText()
    {
        var (router, sender, _) = Create();
        var voice = new MessageAttachment("/tmp/a.wav", "a.wav", "audio/wav", 10, AttachmentKind.Audio);
        var image = new MessageAttachment("/tmp/b.png", "b.png", "image/png", 20, AttachmentKind.Image);

        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_att", Session, "给你"));
        await router.PublishAsync(
            RpcEvents.TurnCompleted,
            Completed("trn_att", attachments: [voice, image]));

        Assert.Equal("给你", Assert.Single(sender.Texts).Text);
        Assert.Equal(["/tmp/a.wav", "/tmp/b.png"], sender.Files.Select(f => f.FilePath));
        Assert.All(sender.Files, f => Assert.Equal(Target, f.Target));
    }

    [Fact]
    public async Task Completed_AttachmentsOnly_StillSendsFiles()
    {
        var (router, sender, _) = Create();
        var file = new MessageAttachment("/tmp/report.pdf", "report.pdf", "application/pdf", 1, AttachmentKind.File);

        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_file", attachments: [file]));

        Assert.Empty(sender.Texts);
        Assert.Single(sender.Files);
    }

    [Fact]
    public async Task Failed_SendsNoticeEvenWithoutText()
    {
        var (router, sender, _) = Create();

        await router.PublishAsync(
            RpcEvents.TurnFailed,
            new TurnFailedEvent("trn_2", Session, "busy", "已取消", Lane, Target));

        Assert.Contains("已取消", Assert.Single(sender.Texts).Text);
    }

    [Fact]
    public async Task EmptyBody_DoesNotPost()
    {
        var (router, sender, _) = Create();

        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_3"));

        Assert.Empty(sender.Texts);
    }

    [Fact]
    public async Task TwoTurns_UseTheirOwnTargets()
    {
        var (router, sender, _) = Create();

        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_a", Session, "一"));
        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_a", replyTarget: "first"));
        await router.PublishAsync(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_b", Session, "二"));
        await router.PublishAsync(RpcEvents.TurnCompleted, Completed("trn_b", replyTarget: "second"));

        Assert.Equal(["first", "second"], sender.Texts.Select(t => t.Target));
        Assert.Equal(["一", "二"], sender.Texts.Select(t => t.Text));
    }

    private static (ChannelReplyRouter Router, RecordingSender Sender, InMemoryDeliveryRepository Ledger) Create()
    {
        var sender = new RecordingSender(Lane);
        var ledger = new InMemoryDeliveryRepository();
        var delivery = new ReliableDelivery([sender], ledger, clock: new FixedClock(DateTimeOffset.UnixEpoch));
        var router = new ChannelReplyRouter(delivery, [], NullLogger<ChannelReplyRouter>.Instance);
        return (router, sender, ledger);
    }

    private static TurnCompletedEvent Completed(
        string turnId,
        string sessionId = Session,
        string? replyTarget = Target,
        IReadOnlyList<MessageAttachment>? attachments = null) => new(
        turnId,
        sessionId,
        MessageId: 1,
        new UsageSummary(1, 10, 20, 0, 10),
        Lane,
        replyTarget,
        attachments);
}
