using Xunit;

namespace Shoyee.Photo.Tests;

/// <summary>异步队列：成功才投递；失败只记日志（与 Harem 一致，不往会话塞失败消息）。</summary>
public sealed class PhotoTaskRunnerTests
{
    [Fact]
    public async Task RunAsync_DeliversAssetsWithOriginalContext()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var delivery = new FakeDelivery();
            var runner = new PhotoTaskRunner(CreateService(root), delivery);
            var context = new PhotoTaskContext("task-1", "desktop:roleplay", null, "角色图");

            var ok = await runner.RunAsync(new PhotoJob(
                new PhotoPlan("bedroom", "casual", "standing"),
                context));

            Assert.True(ok);
            var sent = Assert.Single(delivery.Items);
            Assert.Equal(context, sent.Context);
            Assert.Single(sent.Assets);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Fact]
    public async Task RunAsync_ProviderFailure_DoesNotDeliver()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var delivery = new FakeDelivery();
            var runner = new PhotoTaskRunner(CreateService(root, failing: true), delivery);

            var ok = await runner.RunAsync(new PhotoJob(
                new PhotoPlan("bedroom", "casual", "standing"),
                new PhotoTaskContext("task-2", "desktop:roleplay")));

            Assert.False(ok);
            Assert.Empty(delivery.Items);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    private static PhotoService CreateService(string root, bool failing = false)
        => new(
            PhotoTestData.Config(root),
            new PhotoComposer(PhotoTestData.Prompts(root), "xiaoyi"),
            new FakeProvider(failing));

    private sealed class FakeProvider(bool failing) : IPhotoProvider
    {
        public string Name => "fake";

        public bool Configured => true;

        public Task<PhotoResult> GenerateAsync(PhotoRequest request, CancellationToken cancellationToken = default)
            => failing
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(new PhotoResult([new PhotoImage("image/png", [1])]));
    }

    private sealed class FakeDelivery : IPhotoDelivery
    {
        public List<PhotoDelivery> Items { get; } = [];

        public Task DeliverAsync(PhotoDelivery delivery, CancellationToken cancellationToken = default)
        {
            Items.Add(delivery);
            return Task.CompletedTask;
        }
    }
}
