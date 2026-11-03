using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Channels.Delivery;
using Shoyee.Channels.Tests.TestSupport;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Channels.Tests.Delivery;

/// <summary>投递台账：先挂账再发、失败留待补发、没发送器就不挂账。</summary>
public sealed class ReliableDeliveryTests
{
    private const string Lane = "mattermost:work";

    [Fact]
    public async Task SendText_Success_MarksSent()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new RecordingSender(Lane);
        var delivery = new ReliableDelivery([sender], ledger, clock: new FixedClock(DateTimeOffset.UnixEpoch));

        await delivery.SendTextAsync(Lane, "ses_1", "mm:channel:c1", "在");

        Assert.Equal(("mm:channel:c1", "在"), Assert.Single(sender.Texts));
        Assert.Equal(DeliveryState.Sent, Assert.Single(ledger.Rows).State);
    }

    [Fact]
    public async Task RoleplayText_SplitsOnSingleNewline()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new RecordingSender(Lane);
        var lanes = new InMemoryLaneRepository();
        lanes.Seed(LaneAt(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Mode.Roleplay));
        var delivery = new ReliableDelivery(
            [sender],
            ledger,
            clock: new FixedClock(DateTimeOffset.UnixEpoch),
            lanes: lanes);

        await delivery.SendTextAsync(Lane, null, "mm:channel:c1", "一\n二<br/>三\n\n四");

        Assert.Equal(["一", "二", "三", "四"], sender.Texts.Select(static text => text.Text));
        Assert.Equal(4, ledger.Rows.Count);
        Assert.All(ledger.Rows, static row => Assert.Equal(DeliveryState.Sent, row.State));
    }

    [Fact]
    public async Task WorkText_StaysOneMessage()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new RecordingSender(Lane);
        var lanes = new InMemoryLaneRepository();
        lanes.Seed(LaneAt(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Mode.Work));
        var delivery = new ReliableDelivery(
            [sender],
            ledger,
            clock: new FixedClock(DateTimeOffset.UnixEpoch),
            lanes: lanes);

        await delivery.SendTextAsync(Lane, null, "mm:channel:c1", "一\n二");

        Assert.Single(sender.Texts);
        Assert.Single(ledger.Rows);
    }

    [Fact]
    public async Task RoleplayWeChatText_StaysOneMessage()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new RecordingSender(Lane);
        var lanes = new InMemoryLaneRepository();
        lanes.Seed(LaneAt(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Mode.Roleplay, LaneChannel.WeChat));
        var delivery = new ReliableDelivery(
            [sender],
            ledger,
            clock: new FixedClock(DateTimeOffset.UnixEpoch),
            lanes: lanes);

        await delivery.SendTextAsync(Lane, null, "wx:peer", "一\n二");

        Assert.Single(sender.Texts);
        Assert.Single(ledger.Rows);
    }

    [Fact]
    public async Task SendText_Failure_KeepsRowForRetry()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new RecordingSender(Lane, new HttpRequestException("网络断了"));
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var delivery = new ReliableDelivery([sender], ledger, clock: clock);

        await delivery.SendTextAsync(Lane, "ses_1", "mm:channel:c1", "在");

        var row = Assert.Single(ledger.Rows);
        Assert.Equal(DeliveryState.Failed, row.State);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("网络断了", row.LastError);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(5), row.NextAttemptAt);
    }

    [Fact]
    public async Task NoSender_DoesNotEnqueue()
    {
        var ledger = new InMemoryDeliveryRepository();
        var delivery = new ReliableDelivery([], ledger, clock: new FixedClock(DateTimeOffset.UnixEpoch));

        await delivery.SendTextAsync(Lane, null, "mm:channel:c1", "在");

        Assert.Empty(ledger.Rows);
    }

    [Fact]
    public async Task Flush_RetriesDueRow_AndMarksSent()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new FlakySender(Lane);
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var delivery = new ReliableDelivery([sender], ledger, clock: clock);

        await delivery.SendTextAsync(Lane, null, "mm:channel:c1", "在");
        Assert.Equal(DeliveryState.Failed, Assert.Single(ledger.Rows).State);

        sender.Healthy = true;
        clock.Advance(TimeSpan.FromSeconds(5));
        await delivery.FlushAsync();

        Assert.Equal(DeliveryState.Sent, Assert.Single(ledger.Rows).State);
        Assert.Single(sender.Texts);
    }

    [Fact]
    public async Task Flush_SkipsRowsNotYetDue()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new FlakySender(Lane);
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var delivery = new ReliableDelivery([sender], ledger, clock: clock);

        await delivery.SendTextAsync(Lane, null, "mm:channel:c1", "在");
        sender.Healthy = true;

        // 退避 5s，此刻还没到点
        await delivery.FlushAsync();

        Assert.Equal(DeliveryState.Failed, Assert.Single(ledger.Rows).State);
        Assert.Empty(sender.Texts);
    }

    [Fact]
    public async Task SendFile_UsesFilePath()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new RecordingSender(Lane);
        var delivery = new ReliableDelivery([sender], ledger, clock: new FixedClock(DateTimeOffset.UnixEpoch));

        await delivery.SendFileAsync(Lane, "ses_1", "mm:channel:c1", "/tmp/a.png", "看图");

        var file = Assert.Single(sender.Files);
        Assert.Equal("/tmp/a.png", file.FilePath);
        Assert.Equal("看图", file.Caption);
        Assert.Equal("file", Assert.Single(ledger.Rows).Kind);
    }

    [Fact]
    public async Task UntrustedSender_FailureIsTreatedAsDelivered()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new UntrustedSender(Lane);
        var delivery = new ReliableDelivery([sender], ledger, clock: new FixedClock(DateTimeOffset.UnixEpoch));

        await delivery.SendTextAsync(Lane, null, "wx:peer", "在");

        var row = Assert.Single(ledger.Rows);
        Assert.Equal(DeliveryState.Sent, row.State);
        Assert.Equal(1, sender.Attempts);
    }

    [Fact]
    public async Task UntrustedSender_PendingLeftoverIsSettledWithoutResending()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new UntrustedSender(Lane);
        var delivery = new ReliableDelivery([sender], ledger, clock: new FixedClock(DateTimeOffset.UnixEpoch));

        // 模拟「挂上台账但进程在发送前挂了」的残留
        await ledger.EnqueueAsync(Lane, null, "message", "{\"kind\":\"text\",\"target\":\"wx:peer\",\"text\":\"在\"}");

        await delivery.FlushLaneAsync(Lane);

        Assert.Equal(DeliveryState.Sent, Assert.Single(ledger.Rows).State);
        Assert.Equal(0, sender.Attempts);
    }

    /// <summary>入队那一份还在通道上慢慢发时，补发轮询不能把同一条再发一遍。</summary>
    [Fact]
    public async Task Flush_DoesNotResendRowStillInFlight()
    {
        var ledger = new InMemoryDeliveryRepository();
        var sender = new BlockingSender(Lane);
        var delivery = new ReliableDelivery([sender], ledger, clock: new FixedClock(DateTimeOffset.UnixEpoch));

        var send = delivery.SendFileAsync(Lane, null, "mm:channel:c1", "/tmp/a.png");
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 此刻行还是 Pending、通道调用没返回：补发轮询会选中它，但不该再发
        await delivery.FlushAsync();

        sender.Release.TrySetResult();
        await send;

        Assert.Equal(1, sender.FileCount);
        Assert.Equal(DeliveryState.Sent, Assert.Single(ledger.Rows).State);
    }

    /// <summary>报失败但结果不可信的发送器（微信式）。</summary>
    private sealed class UntrustedSender : IChannelSender
    {
        public UntrustedSender(string laneId) => LaneId = laneId;

        public string LaneId { get; }

        public bool OutcomeTrusted => false;

        public int Attempts { get; private set; }

        public Task SendTextAsync(string target, string text, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.FromException(new HttpRequestException("结果不明"));
        }

        public Task SendFileAsync(
            string target,
            string filePath,
            string? caption,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.FromException(new HttpRequestException("结果不明"));
        }
    }

    /// <summary>先失败、之后可恢复的假发送器。</summary>
    [Fact]
    public async Task Send_RecordsAttempt_WithSecondsSinceLastInbound()
    {
        var now = DateTimeOffset.UnixEpoch;
        var lanes = new InMemoryLaneRepository();
        lanes.Seed(LaneAt(now.AddSeconds(-42), now));
        var attempts = new RecordingAttempts();
        var delivery = new ReliableDelivery(
            [new RecordingSender(Lane)],
            new InMemoryDeliveryRepository(),
            clock: new FixedClock(now),
            lanes: lanes,
            attempts: attempts);

        await delivery.SendTextAsync(Lane, null, "mm:channel:c1", "在");

        var record = Assert.Single(attempts.Rows);
        Assert.Equal(Lane, record.LaneId);
        Assert.Equal("ok", record.Result);
        Assert.Equal(42, record.SinceLastInboundSeconds);
        Assert.Null(record.Error);
    }

    [Fact]
    public async Task Send_RecordsFailureCategory_ForWindowClosed()
    {
        var now = DateTimeOffset.UnixEpoch;
        var attempts = new RecordingAttempts();
        var delivery = new ReliableDelivery(
            [new RecordingSender(Lane, new ChannelSendException(ChannelSendFailure.WindowClosed, "窗口外"))],
            new InMemoryDeliveryRepository(),
            clock: new FixedClock(now),
            attempts: attempts);

        await delivery.SendTextAsync(Lane, null, "mm:channel:c1", "在");

        var record = Assert.Single(attempts.Rows);
        Assert.Equal("window_closed", record.Result);
        Assert.Equal("窗口外", record.Error);
        Assert.Equal(0, record.SinceLastInboundSeconds);
    }

    [Fact]
    public async Task Send_NoAttemptRecorder_DoesNotFail()
    {
        var delivery = new ReliableDelivery(
            [new RecordingSender(Lane)],
            new InMemoryDeliveryRepository(),
            clock: new FixedClock(DateTimeOffset.UnixEpoch));

        await delivery.SendTextAsync(Lane, null, "mm:channel:c1", "在");
    }

    private static LaneRecord LaneAt(
        DateTimeOffset inbound,
        DateTimeOffset created,
        Mode mode = Mode.Work,
        LaneChannel channel = LaneChannel.Mattermost)
        => new(
            Lane,
            channel,
            mode,
            "ext",
            "工作",
            null,
            inbound,
            inbound,
            created);

    private sealed class RecordingAttempts : Shoyee.Storage.Abstractions.ISendAttemptRepository
    {
        public List<Shoyee.Storage.Abstractions.SendAttemptRecord> Rows { get; } = [];

        public Task AddAsync(
            Shoyee.Storage.Abstractions.SendAttemptRecord record,
            CancellationToken cancellationToken = default)
        {
            Rows.Add(record);
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetLastSuccessAsync(string laneId, CancellationToken cancellationToken = default)
            => Task.FromResult<DateTimeOffset?>(Rows.LastOrDefault(r => r.Result == "ok") is { } ok ? DateTimeOffset.UtcNow : null);
    }

    /// <summary>卡在发送里的假发送器：用来把「正在发」这一刻定住。</summary>
    private sealed class BlockingSender : IChannelSender
    {
        public BlockingSender(string laneId) => LaneId = laneId;

        public string LaneId { get; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int FileCount { get; private set; }

        public Task SendTextAsync(string target, string text, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task SendFileAsync(
            string target,
            string filePath,
            string? caption,
            CancellationToken cancellationToken = default)
        {
            FileCount++;
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
        }
    }

    private sealed class FlakySender : IChannelSender    {
        public FlakySender(string laneId) => LaneId = laneId;

        public string LaneId { get; }

        public bool Healthy { get; set; }

        public List<string> Texts { get; } = [];

        public Task SendTextAsync(string target, string text, CancellationToken cancellationToken = default)
        {
            if (!Healthy)
            {
                return Task.FromException(new HttpRequestException("挂了"));
            }

            Texts.Add(text);
            return Task.CompletedTask;
        }

        public Task SendFileAsync(
            string target,
            string filePath,
            string? caption,
            CancellationToken cancellationToken = default)
            => Healthy ? Task.CompletedTask : Task.FromException(new HttpRequestException("挂了"));
    }
}
