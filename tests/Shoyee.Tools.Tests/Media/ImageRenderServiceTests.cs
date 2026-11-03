using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Tools.Media;
using Shoyee.Tools.Tests.Photo;
using Xunit;

namespace Shoyee.Tools.Tests.Media;

/// <summary><c>image.render</c>：按提示词同步出图、落 assets/images、返回可被 asset.get 取回的资产。</summary>
public sealed class ImageRenderServiceTests : IDisposable
{
    private readonly string _root = PhotoTestSupport.NewRoot();

    public void Dispose() => PhotoTestSupport.TryDelete(_root);

    [Fact]
    public async Task Render_按提示词出图并落盘()
    {
        var provider = new PhotoTestSupport.FakeProvider();
        var store = new AssetStore(_root);
        var service = new ImageRenderService(PhotoTestSupport.Service(_root, provider), store);

        var result = await service.RenderAsync(
            new ImageRenderRequest("a cozy study", Negative: "blurry", AspectRatio: "1:1"));

        Assert.True(File.Exists(result.Path));
        Assert.Equal("image/png", result.Mime);
        Assert.Equal(2, result.SizeBytes);
        Assert.Contains("assets/images", result.AssetId, StringComparison.Ordinal);
        Assert.NotNull(store.Get(result.AssetId));

        Assert.Equal("a cozy study", provider.LastRequest!.Positive);
        Assert.Equal("blurry", provider.LastRequest.Negative);
        Assert.Equal(1024, provider.LastRequest.Width);
        Assert.Equal(1024, provider.LastRequest.Height);
    }

    [Fact]
    public async Task Render_比例不认识时回落竖屏()
    {
        var provider = new PhotoTestSupport.FakeProvider();
        var service = new ImageRenderService(
            PhotoTestSupport.Service(_root, provider),
            new AssetStore(_root));

        await service.RenderAsync(new ImageRenderRequest("x", AspectRatio: "4:5"));

        Assert.Equal(832, provider.LastRequest!.Width);
        Assert.Equal(1216, provider.LastRequest.Height);
    }

    [Fact]
    public async Task Render_提示词为空_报invalid_params()
    {
        var service = new ImageRenderService(
            PhotoTestSupport.Service(_root),
            new AssetStore(_root));

        var error = await Assert.ThrowsAsync<RpcException>(
            () => service.RenderAsync(new ImageRenderRequest("   ")));

        Assert.Equal(RpcErrors.InvalidParams, error.Code);
    }

    [Fact]
    public async Task Render_供应商失败_折成internal()
    {
        var service = new ImageRenderService(
            PhotoTestSupport.Service(_root, new PhotoTestSupport.FakeProvider(failing: true)),
            new AssetStore(_root));

        var error = await Assert.ThrowsAsync<RpcException>(
            () => service.RenderAsync(new ImageRenderRequest("x")));

        Assert.Equal(RpcErrors.Internal, error.Code);
        Assert.Contains("出图失败", error.Message, StringComparison.Ordinal);
    }
}
