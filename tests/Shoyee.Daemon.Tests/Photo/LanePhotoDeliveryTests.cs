using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Channels.Delivery;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Daemon.Photo;
using Shoyee.Daemon.Tests.TestSupport;
using Shoyee.Gateway.Subscriptions;
using Shoyee.Photo;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Daemon.Tests.Photo;

/// <summary>出图投递：桌面只推事件，IM 走台账传文件，没有回投地址就只推事件。</summary>
public sealed class LanePhotoDeliveryTests
{
    private const string DesktopLane = "desktop:roleplay";
    private const string ImLane = "mattermost:roleplay";

    [Fact]
    public async Task DesktopLane_DoesNotUpload()
    {
        var (delivery, sender, lanes) = Create();
        await lanes.UpsertAsync(Lane(DesktopLane, LaneChannel.Desktop, target: null));

        await delivery.DeliverAsync(Photo(DesktopLane));

        Assert.Empty(sender.Files);
    }

    [Fact]
    public async Task ImLane_SendsEveryAssetWithCaptionOnFirst()
    {
        var (delivery, sender, lanes) = Create();
        await lanes.UpsertAsync(Lane(ImLane, LaneChannel.Mattermost, target: "mm:channel:c1"));

        await delivery.DeliverAsync(Photo(ImLane, assets: 2, caption: "想你了"));

        Assert.Equal(2, sender.Files.Count);
        Assert.All(sender.Files, f => Assert.Equal("mm:channel:c1", f.Target));
        Assert.Equal("想你了", sender.Files[0].Caption);
        Assert.Null(sender.Files[1].Caption);
    }

    [Fact]
    public async Task ImLaneWithoutTarget_DoesNotUpload()
    {
        var (delivery, sender, lanes) = Create();
        await lanes.UpsertAsync(Lane(ImLane, LaneChannel.Mattermost, target: null));

        await delivery.DeliverAsync(Photo(ImLane));

        Assert.Empty(sender.Files);
    }

    [Fact]
    public async Task UnknownLane_DoesNotUpload()
    {
        var (delivery, sender, _) = Create();

        await delivery.DeliverAsync(Photo(ImLane));

        Assert.Empty(sender.Files);
    }

    private static (LanePhotoDelivery Delivery, RecordingSender Sender, InMemoryLaneRepository Lanes) Create()
    {
        var lanes = new InMemoryLaneRepository();
        var sender = new RecordingSender();
        var ledger = new InMemoryDeliveryRepository();
        var reliable = new ReliableDelivery([sender], ledger);
        var delivery = new LanePhotoDelivery(
            new EventHub(NullLogger<EventHub>.Instance),
            lanes,
            reliable,
            NullLogger<LanePhotoDelivery>.Instance);
        return (delivery, sender, lanes);
    }

    private static LaneRecord Lane(string id, LaneChannel channel, string? target) => new(
        id,
        channel,
        Mode.Roleplay,
        channel == LaneChannel.Desktop ? "desktop" : "http://mm",
        "测试",
        ActiveSessionId: null,
        LastInboundAt: null,
        LastInteractionAt: null,
        DateTimeOffset.UnixEpoch,
        target);

    private static PhotoDelivery Photo(string laneId, int assets = 1, string? caption = null) => new(
        new PhotoTaskContext("task_1", laneId, null, caption),
        Enumerable.Range(0, assets)
            .Select(i => new PhotoAsset($"/tmp/{i}.png", "image/png", 10))
            .ToList());

    private sealed class RecordingSender : IChannelSender
    {
        public string LaneId => ImLane;

        public List<(string Target, string FilePath, string? Caption)> Files { get; } = [];

        public Task SendTextAsync(string target, string text, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendFileAsync(
            string target,
            string filePath,
            string? caption,
            CancellationToken cancellationToken = default)
        {
            Files.Add((target, filePath, caption));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryDeliveryRepository : IDeliveryRepository
    {
        private long _nextId = 1;

        public List<DeliveryRecord> Rows { get; } = [];

        public Task<DeliveryRecord> EnqueueAsync(
            string laneId,
            string? sessionId,
            string kind,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            var row = new DeliveryRecord(
                _nextId++,
                laneId,
                sessionId,
                kind,
                payloadJson,
                DeliveryState.Pending,
                0,
                null,
                null,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
            Rows.Add(row);
            return Task.FromResult(row);
        }

        public Task<IReadOnlyList<DeliveryRecord>> GetDueAsync(
            DateTimeOffset now,
            int limit = 20,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeliveryRecord>>([]);

        public Task<IReadOnlyList<DeliveryRecord>> GetPendingForLaneAsync(
            string laneId,
            int limit = 20,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeliveryRecord>>(
                Rows.Where(r => r.LaneId == laneId && r.State is DeliveryState.Pending or DeliveryState.Failed)
                    .Take(limit)
                    .ToList());

        public Task MarkSentAsync(long id, CancellationToken cancellationToken = default)
        {
            Replace(id, r => r with { State = DeliveryState.Sent });
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            long id,
            string error,
            DateTimeOffset nextAttemptAt,
            int maxAttempts = 10,
            CancellationToken cancellationToken = default)
        {
            Replace(id, r => r with { State = DeliveryState.Failed, Attempts = r.Attempts + 1, LastError = error });
            return Task.CompletedTask;
        }

        private void Replace(long id, Func<DeliveryRecord, DeliveryRecord> update)
        {
            var index = Rows.FindIndex(r => r.Id == id);
            if (index >= 0)
            {
                Rows[index] = update(Rows[index]);
            }
        }
    }
}
