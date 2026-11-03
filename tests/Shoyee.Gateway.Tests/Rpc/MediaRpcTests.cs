using Shoyee.Client.Rpc;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;
using Xunit;

namespace Shoyee.Gateway.Tests.Rpc;

/// <summary>本批补齐的方法：会话快照/导出、重试、语音合成、资产读取。</summary>
public sealed class MediaRpcTests
{
    [Fact]
    public async Task SessionSnapshot_ReturnsFrozenText()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<SessionSnapshot>(
            RpcMethods.SessionSnapshot,
            new { sessionId = "ses_1" });

        Assert.Equal("冻结的提示词", result.Text);
        Assert.Equal("ses_1", bed.Sessions.LastSnapshotSessionId);
    }

    [Fact]
    public async Task SessionExport_PassesFormatThrough()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<SessionExportResult>(
            RpcMethods.SessionExport,
            new SessionExportRequest("ses_1", "json"));

        Assert.EndsWith(".md", result.Path, StringComparison.Ordinal);
        Assert.Equal("ses_1", bed.Sessions.LastExport!.SessionId);
        Assert.Equal("json", bed.Sessions.LastExport.Format);
    }

    [Fact]
    public async Task TurnRetry_WorkDesktop_DelegatesToTurnService()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<TurnRef>(
            RpcMethods.TurnRetry,
            new TurnRetryRequest("ses_1"));

        Assert.Equal("trn_retry", result.TurnId);
        Assert.Equal("ses_1", bed.Turns.LastRetry);
    }

    [Fact]
    public async Task TurnRetry_RoleplayDesktop_IsRejected()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.TurnRetry, new TurnRetryRequest("ses_1")));

        Assert.Equal(RpcErrors.InvalidParams, ex.Code);
        Assert.Null(bed.Turns.LastRetry);
    }

    [Fact]
    public async Task TtsSynthesize_ReturnsAsset()
    {
        var tts = new FakeTtsService();
        await using var bed = await GatewayTestBed.StartAsync(tts: tts);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<TtsSynthesizeResult>(
            RpcMethods.TtsSynthesize,
            new TtsSynthesizeRequest(MessageId: 42));

        Assert.Equal("assets/audio/2026/10/a.wav", result.AssetId);
        Assert.Equal(1500, result.DurationMs);
        Assert.Equal(42, tts.LastRequest!.MessageId);
    }

    [Fact]
    public async Task TtsSynthesize_NotConfigured_ReturnsInternal()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.TtsSynthesize, new TtsSynthesizeRequest(MessageId: 42)));

        Assert.Equal(RpcErrors.Internal, ex.Code);
        Assert.Contains("未配置", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImageRender_WorkDesktop_ReturnsAsset()
    {
        var images = new FakeImageService();
        await using var bed = await GatewayTestBed.StartAsync(images: images);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<ImageRenderResult>(
            RpcMethods.ImageRender,
            new ImageRenderRequest("a cozy study", AspectRatio: "1:1"));

        Assert.Equal("assets/images/2026/10/a.png", result.AssetId);
        Assert.Equal(2048, result.SizeBytes);
        Assert.Equal("a cozy study", images.LastRequest!.Prompt);
        Assert.Equal("1:1", images.LastRequest.AspectRatio);
    }

    [Fact]
    public async Task ImageRender_RoleplayDesktop_IsRejected()
    {
        var images = new FakeImageService();
        await using var bed = await GatewayTestBed.StartAsync(images: images);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.ImageRender, new ImageRenderRequest("x")));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
        Assert.Null(images.LastRequest);
    }

    [Fact]
    public async Task ImageRender_NotConfigured_ReturnsInternal()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.ImageRender, new ImageRenderRequest("x")));

        Assert.Equal(RpcErrors.Internal, ex.Code);
        Assert.Contains("出图未配置", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssetGet_RoleplayCanReadAssets()
    {
        await using var bed = await GatewayTestBed.StartAsync(assets: new FakeAssetStore());
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var result = await client.CallAsync<AssetRef>(
            RpcMethods.AssetGet,
            new { assetId = "assets/a.png" });

        Assert.Equal("/tmp/shoyee/assets/a.png", result.Path);
        Assert.Equal("image/png", result.Mime);
    }

    [Fact]
    public async Task AssetGet_UnknownId_IsNotFound()
    {
        await using var bed = await GatewayTestBed.StartAsync(assets: new FakeAssetStore());
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.AssetGet, new { assetId = "assets/missing.png" }));

        Assert.Equal(RpcErrors.NotFound, ex.Code);
    }
}
