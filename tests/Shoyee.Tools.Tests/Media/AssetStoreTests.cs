using Shoyee.Contracts.Dtos;
using Shoyee.Tools.Media;
using Shoyee.Tools.Voice;
using Shoyee.Voice;
using Xunit;

namespace Shoyee.Tools.Tests.Media;

/// <summary>资产 id = 工作区相对路径；只认 assets/，越界一律拒绝。</summary>
public sealed class AssetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "shoyee-assets-" + Guid.NewGuid().ToString("N")[..8]);

    public AssetStoreTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
    }

    [Fact]
    public void Register_返回相对路径与真实信息()
    {
        var path = Path.Combine(_root, "assets", "a.png");
        File.WriteAllBytes(path, [1, 2, 3]);

        var asset = new AssetStore(_root).Register(path);

        Assert.Equal("assets/a.png", asset.AssetId);
        Assert.Equal("image/png", asset.Mime);
        Assert.Equal(3, asset.SizeBytes);
        Assert.True(Path.IsPathRooted(asset.Path));
    }

    [Fact]
    public void Get_越界id一律返回null()
    {
        var store = new AssetStore(_root);

        Assert.Null(store.Get("/etc/passwd"));
        Assert.Null(store.Get("../config.json"));
        Assert.Null(store.Get("assets/../../etc/passwd"));
        Assert.Null(store.Get("assets//a.png"));
        Assert.Null(store.Get("assets/not-there.png"));
    }

    [Fact]
    public void Register_工作区外的文件被拒绝()
    {
        var outside = Path.Combine(Path.GetTempPath(), "shoyee-outside-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(outside, "x");

        Assert.Throws<InvalidOperationException>(() => new AssetStore(_root).Register(outside));
        File.Delete(outside);
    }

    [Fact]
    public async Task TtsService_合成后落盘并返回时长()
    {
        var store = new AssetStore(_root);
        var service = new TtsService(new FakeSynthesizer(Wav(48000)), store, _root);

        var result = await service.SynthesizeAsync(new TtsSynthesizeRequest(Text: "你好"));

        Assert.True(File.Exists(result.Path));
        Assert.Contains("assets/audio", result.Path, StringComparison.Ordinal);
        Assert.Equal("audio/wav", result.Mime);
        Assert.Equal(1000, result.DurationMs);
        // 落盘的文件也能按 assetId 取回
        Assert.NotNull(store.Get(result.AssetId));
    }

    [Fact]
    public async Task TtsService_文本为空_报错()
    {
        var service = new TtsService(new FakeSynthesizer(Wav(0)), new AssetStore(_root), _root);

        var error = await Assert.ThrowsAsync<Shoyee.Contracts.Rpc.RpcException>(
            () => service.SynthesizeAsync(new TtsSynthesizeRequest()));

        Assert.Equal(Shoyee.Contracts.Rpc.RpcErrors.InvalidParams, error.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>24kHz / 16-bit 单声道 WAV；<paramref name="dataBytes"/> = 字节数（2 字节/毫秒）。</summary>
    private static byte[] Wav(int dataBytes)
    {
        var data = new byte[dataBytes];
        var wav = new byte[44 + data.Length];
        using var stream = new MemoryStream(wav);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(24000);
        writer.Write(48000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
        return wav;
    }

    private sealed class FakeSynthesizer(byte[] data) : ISpeechSynthesizer
    {
        public string Name => "fake";

        public bool Configured => true;

        public string VoiceGuide => string.Empty;

        public Task<SynthesizedAudio> SynthesizeAsync(
            SynthesisRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SynthesizedAudio(data, "audio/wav", ".wav"));
    }
}
