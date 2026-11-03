using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Channels.Core;
using Shoyee.Channels.Delivery;
using Shoyee.Channels.Tests.TestSupport;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;using Shoyee.Handlers;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Channels.Tests.Core;

/// <summary>审批旁路：只发非桌面 Lane、按批次合成一条提示、停 typing。</summary>
public sealed class ChannelApprovalRouterTests
{
    private const string Lane = "mattermost:work";
    private const string Session = "ses_1";
    private const string Target = "mm:channel:c1:root";

    [Fact]
    public async Task Request_SendsPromptToLaneTarget()
    {
        var approvals = new StubApprovalConsole();
        approvals.Pending.Add(Request("apv_1", "terminal", "rm -rf /tmp/x"));
        var (router, sender, _) = Create(approvals, lane: LaneRecord(Lane, Target));

        await router.PublishAsync(RpcEvents.ApprovalRequest, approvals.Pending[0]);

        var (target, text) = Assert.Single(sender.Texts);
        Assert.Equal(Target, target);
        Assert.Contains("terminal", text);
        Assert.Contains("rm -rf /tmp/x", text);
        Assert.Contains("/approve", text);
        Assert.Contains("/reject", text);
    }

    [Fact]
    public async Task Batch_ProducesSingleCombinedPrompt()
    {
        var approvals = new StubApprovalConsole();
        approvals.Pending.Add(Request("apv_1", "terminal", "rm -rf /tmp/x"));
        approvals.Pending.Add(Request("apv_2", "file_access_write", "/etc/hosts"));
        var (router, sender, _) = Create(approvals, lane: LaneRecord(Lane, Target));

        await router.PublishAsync(RpcEvents.ApprovalRequest, approvals.Pending[0]);
        await router.PublishAsync(RpcEvents.ApprovalRequest, approvals.Pending[1]);

        var (_, text) = Assert.Single(sender.Texts);
        Assert.Contains("2 个操作", text);
        Assert.Contains("terminal", text);
        Assert.Contains("file_access_write", text);
    }

    [Fact]
    public async Task DesktopLane_IsSkipped()
    {
        var approvals = new StubApprovalConsole();
        approvals.Pending.Add(Request("apv_1", "terminal", "rm -rf /tmp/x"));
        var (router, sender, _) = Create(
            approvals,
            lane: LaneRecord("desktop:work", Target) with { Channel = LaneChannel.Desktop });

        await router.PublishAsync(RpcEvents.ApprovalRequest, approvals.Pending[0]);

        Assert.Empty(sender.Texts);
    }

    [Fact]
    public async Task LaneWithoutTarget_IsSkipped()
    {
        var approvals = new StubApprovalConsole();
        approvals.Pending.Add(Request("apv_1", "terminal", "rm -rf /tmp/x"));
        var (router, sender, _) = Create(approvals, lane: LaneRecord(Lane, null));

        await router.PublishAsync(RpcEvents.ApprovalRequest, approvals.Pending[0]);

        Assert.Empty(sender.Texts);
    }

    [Fact]
    public async Task UnknownLane_IsSkipped()
    {
        var approvals = new StubApprovalConsole();
        approvals.Pending.Add(Request("apv_1", "terminal", "rm -rf /tmp/x"));
        var (router, sender, _) = Create(approvals, lane: null);

        await router.PublishAsync(RpcEvents.ApprovalRequest, approvals.Pending[0]);

        Assert.Empty(sender.Texts);
    }

    private static (ChannelApprovalRouter Router, RecordingSender Sender, InMemoryDeliveryRepository Ledger) Create(
        StubApprovalConsole approvals,
        LaneRecord? lane)
    {
        var lanes = new InMemoryLaneRepository();
        if (lane is not null)
        {
            lanes.Seed(lane);
        }

        var sender = new RecordingSender(Lane);
        var ledger = new InMemoryDeliveryRepository();
        var delivery = new ReliableDelivery([sender], ledger, clock: new FixedClock(DateTimeOffset.UnixEpoch));
        var router = new ChannelApprovalRouter(
            delivery,
            lanes,
            new SingleServiceProvider(approvals),
            [],
            NullLogger<ChannelApprovalRouter>.Instance);
        return (router, sender, ledger);
    }

    private sealed class SingleServiceProvider(IApprovalConsole approvals) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IApprovalConsole) ? approvals : null;
    }

    private static LaneRecord LaneRecord(string id, string? lastTarget) => new(
        id,
        LaneChannel.Mattermost,
        Mode.Work,
        "http://mm",
        "工作",
        ActiveSessionId: Session,
        LastInboundAt: null,
        LastInteractionAt: null,
        DateTimeOffset.UnixEpoch,
        LastTarget: lastTarget);

    private static ApprovalRequestDto Request(string id, string tool, string command) => new(
        id,
        Session,
        "trn_1",
        tool,
        command,
        Arguments: null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddMinutes(10),
        Lane);
}
