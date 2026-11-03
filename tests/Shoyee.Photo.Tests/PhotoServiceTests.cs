using Xunit;

namespace Shoyee.Photo.Tests;

/// <summary>落盘：供应商只回字节，目录与扩展名由服务决定。</summary>
public sealed class PhotoServiceTests
{
    [Fact]
    public async Task Generate_WritesUnderAssetsImages()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var provider = new FakeProvider(new PhotoImage("image/png", [1, 2, 3], ".png"));
            var service = new PhotoService(
                PhotoTestData.Config(root),
                new PhotoComposer(PhotoTestData.Prompts(root), "xiaoyi"),
                provider);

            var assets = await service.GenerateAsync(
                new PhotoPlan("bedroom", "casual", "standing"),
                new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero));

            var asset = Assert.Single(assets);
            Assert.StartsWith(Path.Combine(root, "assets", "images"), asset.Path);
            Assert.EndsWith(".png", asset.Path);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(asset.Path));
            Assert.Equal("image/png", asset.Mime);
            Assert.Equal(3, asset.SizeBytes);
            Assert.Equal("xiaoyi", provider.LastRequest!.FileNamePrefix);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Fact]
    public async Task Generate_DirectRequestSkipsVocabulary()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            var provider = new FakeProvider(new PhotoImage("image/jpeg", [9], ".jpg"));
            var service = new PhotoService(
                PhotoTestData.Config(root),
                new PhotoComposer(new PhotoPrompts(Path.Combine(root, "missing")), null),
                provider);

            var assets = await service.GenerateAsync(new PhotoRequest("a cat", "", 512, 512, 7));

            Assert.Equal(".jpg", Path.GetExtension(Assert.Single(assets).Path));
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Fact]
    public async Task Generate_EmptyResult_Throws()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            var service = new PhotoService(
                PhotoTestData.Config(root),
                new PhotoComposer(new PhotoPrompts(Path.Combine(root, "missing")), null),
                new FakeProvider());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateAsync(new PhotoRequest("x", "", 1, 1, 1)));
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    private sealed class FakeProvider(params PhotoImage[] images) : IPhotoProvider
    {
        public string Name => "fake";

        public bool Configured => true;

        public PhotoRequest? LastRequest { get; private set; }

        public Task<PhotoResult> GenerateAsync(PhotoRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new PhotoResult(images));
        }
    }
}
