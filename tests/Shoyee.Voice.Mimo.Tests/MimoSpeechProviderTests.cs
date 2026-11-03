using System.Net;
using System.Text;
using System.Text.Json;
using Shoyee.Voice;
using Shoyee.Voice.Mimo;
using Xunit;

namespace Shoyee.Voice.Mimo.Tests;

/// <summary>
/// MiMo 语音客户端：合成/转写请求体、响应解析、重试与错误提示。
/// 不连真网——HttpClient 由 <see cref="StubHandler"/> 假造。
/// </summary>
public sealed class MimoSpeechProviderTests
{
    private static readonly byte[] SampleAudio = Encoding.ASCII.GetBytes("RIFF0000WAVEfake-audio-bytes");

    [Fact]
    public async Task Synthesize_DecodesBase64Audio()
    {
        string? body = null;
        using var provider = Create(new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(AudioJson("你好"));
        }));

        var audio = await provider.SynthesizeAsync(new SynthesisRequest("你好"));

        Assert.Equal(SampleAudio, audio.Data);
        Assert.Equal("audio/wav", audio.Mime);
        Assert.Equal(".wav", audio.Extension);
        Assert.Equal("你好", audio.Transcript);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("mimo-v2.5-tts", root.GetProperty("model").GetString());
        Assert.Equal("wav", root.GetProperty("audio").GetProperty("format").GetString());
        Assert.Equal("mimo_default", root.GetProperty("audio").GetProperty("voice").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("assistant", messages[0].GetProperty("role").GetString());
        Assert.Equal("你好", messages[0].GetProperty("content").GetString());
        Assert.False(root.TryGetProperty("asr_options", out _));
    }

    [Fact]
    public async Task Synthesize_Style_BecomesUserMessage()
    {
        string? body = null;
        using var provider = Create(new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return AudioResponse();
        }));

        await provider.SynthesizeAsync(new SynthesisRequest("正文", Style: "温柔一点", Voice: "冰糖"));

        using var doc = JsonDocument.Parse(body!);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("温柔一点", messages[0].GetProperty("content").GetString());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("冰糖", doc.RootElement.GetProperty("audio").GetProperty("voice").GetString());
    }

    [Fact]
    public async Task Synthesize_DesignPrompt_SwitchesModelAndDropsVoice()
    {
        string? body = null;
        using var provider = Create(new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return AudioResponse();
        }), new MimoVoiceOptions(DesignPrompt: "年轻女性的轻柔嗓音"));

        await provider.SynthesizeAsync(new SynthesisRequest("正文"));

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("mimo-v2.5-tts-voicedesign", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("audio").TryGetProperty("voice", out _));
        Assert.Equal("年轻女性的轻柔嗓音", root.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Synthesize_EmptyAudio_Throws()
    {
        using var provider = Create(new StubHandler(_ => Json("""{"choices":[{"message":{"content":"","audio":null}}]}""")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SynthesizeAsync(new SynthesisRequest("你好")));

        Assert.Contains("没有返回音频", ex.Message);
    }

    [Fact]
    public async Task Synthesize_MissingApiKey_ExplainsEnvVar()
    {
        using var provider = Create(
            new StubHandler(_ => AudioResponse()),
            new MimoVoiceOptions(ApiKeyEnv: "SHOYEE_TEST_NO_SUCH_KEY"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SynthesizeAsync(new SynthesisRequest("你好")));

        Assert.Contains("SHOYEE_TEST_NO_SUCH_KEY", ex.Message);
    }

    [Fact]
    public async Task Transcribe_ReturnsMessageContent()
    {
        string? body = null;
        var audioPath = WriteTempWav(out var cleanup);
        try
        {
            using var provider = Create(new StubHandler(request =>
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json("""{"choices":[{"message":{"role":"assistant","content":" 晚上好 "}}]}""");
            }));

            var result = await provider.TranscribeAsync(audioPath);

            Assert.NotNull(result);
            Assert.Equal("晚上好", result!.Text);

            using var doc = JsonDocument.Parse(body!);
            var root = doc.RootElement;
            Assert.Equal("mimo-v2.5-asr", root.GetProperty("model").GetString());
            // asr_options 是请求体顶层字段（不是 extra_body 包一层）
            Assert.Equal("zh", root.GetProperty("asr_options").GetProperty("language").GetString());
            var part = root.GetProperty("messages")[0].GetProperty("content")[0];
            Assert.Equal("input_audio", part.GetProperty("type").GetString());
            Assert.StartsWith(
                "data:audio/wav;base64,",
                part.GetProperty("input_audio").GetProperty("data").GetString());
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task Transcribe_RejectsNonWavMp3()
    {
        using var provider = Create(new StubHandler(_ => Json("{}")));
        var path = Path.Combine(Path.GetTempPath(), $"voice-{Guid.NewGuid():N}.silk");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        try
        {
            var ex = await Assert.ThrowsAsync<NotSupportedException>(() => provider.TranscribeAsync(path));
            Assert.Contains("wav", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Transcribe_EmptyBody_ReturnsNull()
    {
        var audioPath = WriteTempWav(out var cleanup);
        try
        {
            using var provider = Create(new StubHandler(
                _ => Json("""{"choices":[{"message":{"content":"   "}}]}""")));

            Assert.Null(await provider.TranscribeAsync(audioPath));
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task Retry_On429_ThenSucceeds()
    {
        var calls = 0;
        using var provider = Create(
            new StubHandler(_ =>
            {
                calls++;
                return calls == 1
                    ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    : AudioResponse();
            }),
            new MimoVoiceOptions(TimeoutSeconds: 5));

        var audio = await provider.SynthesizeAsync(new SynthesisRequest("你好"));

        Assert.Equal(SampleAudio, audio.Data);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Configured_FollowsBaseUrl()
    {
        using var provider = new MimoSpeechProvider(new MimoVoiceOptions(BaseUrl: ""));
        Assert.False(provider.Configured);
    }

    [Fact]
    public void UnsupportedFormat_IsRejectedAtConstruction()
    {
        Assert.Throws<InvalidOperationException>(
            () => new MimoSpeechProvider(new MimoVoiceOptions(Format: "pcm16")));
    }

    private static MimoSpeechProvider Create(HttpMessageHandler handler, MimoVoiceOptions? options = null)
        => new(
            options ?? new MimoVoiceOptions(ApiKeyEnv: Environment.GetEnvironmentVariable("XIAOMI_API_KEY") is { Length: > 0 }
                ? "XIAOMI_API_KEY"
                : EnsureTestKey()),
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });

    /// <summary>测试机没有 XIAOMI_API_KEY 时造一个占位 key（不会真的发出去）。</summary>
    private static string EnsureTestKey()
    {
        const string name = "SHOYEE_TEST_MIMO_KEY";
        Environment.SetEnvironmentVariable(name, "test-key");
        return name;
    }

    private static HttpResponseMessage AudioResponse()
        => Json(AudioJson(transcript: null));

    private static string AudioJson(string? transcript)
        => "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"audio\":{\"data\":\""
           + Convert.ToBase64String(SampleAudio)
           + "\""
           + (transcript is null ? string.Empty : ",\"transcript\":\"" + transcript + "\"")
           + "}}}]}";

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string WriteTempWav(out Action cleanup)
    {
        var path = Path.Combine(Path.GetTempPath(), $"voice-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, SampleAudio);
        cleanup = () =>
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        };
        return path;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_responder(request));
        }
    }
}
