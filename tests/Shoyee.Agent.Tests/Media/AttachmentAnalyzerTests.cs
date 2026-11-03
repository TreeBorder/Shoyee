using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Media;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Voice;
using Xunit;

namespace Shoyee.Agent.Tests.Media;

/// <summary>入站附件自动分析：图片走视觉、语音走转写，失败/关闭时静默跳过。</summary>
public sealed class AttachmentAnalyzerTests
{
    [Fact]
    public async Task Image_IsDescribedByVisionEndpoint()
    {
        var path = await NewImageAsync();
        try
        {
            var vision = new FakeVisionClient("一只橘猫躺在窗台上。");
            var analyzer = new AttachmentAnalyzer(new AttachmentOptions(), () => vision, null);

            var results = await analyzer.AnalyzeAsync([Image(path)]);

            var analysis = Assert.Single(results);
            Assert.Contains("图片内容", analysis.Text);
            Assert.Contains("一只橘猫", analysis.Text);
            Assert.Equal(path, analysis.Path);

            var sent = Assert.Single(vision.Calls);
            Assert.Contains(sent, content => content is DataContent);
            Assert.Contains(sent, content => content is TextContent text && text.Text.Contains("描述"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Audio_IsTranscribed()
    {
        var path = Path.Combine(Path.GetTempPath(), "shoyee-analyzer-" + Guid.NewGuid().ToString("N") + ".wav");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        try
        {
            var transcriber = new FakeTranscriber("晚上好");
            var analyzer = new AttachmentAnalyzer(new AttachmentOptions(), null, transcriber);

            var results = await analyzer.AnalyzeAsync([new MessageAttachment(path, "a.wav", "audio/wav", 3, AttachmentKind.Audio)]);

            var analysis = Assert.Single(results);
            Assert.Contains("语音转写", analysis.Text);
            Assert.Contains("晚上好", analysis.Text);
            Assert.Equal(path, transcriber.LastPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Disabled_ReturnsNothing()
    {
        var path = await NewImageAsync();
        try
        {
            var vision = new FakeVisionClient("不该被调用");
            var analyzer = new AttachmentAnalyzer(new AttachmentOptions(AutoAnalyze: false), () => vision, null);

            Assert.Empty(await analyzer.AnalyzeAsync([Image(path)]));
            Assert.Empty(vision.Calls);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VisionFailure_DegradesSilently()
    {
        var path = await NewImageAsync();
        try
        {
            var analyzer = new AttachmentAnalyzer(new AttachmentOptions(), () => new ThrowingVisionClient(), null);

            Assert.Empty(await analyzer.AnalyzeAsync([Image(path)]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingVisionOrTranscriber_Skips()
    {
        var path = await NewImageAsync();
        try
        {
            var analyzer = new AttachmentAnalyzer(new AttachmentOptions(), () => null, null);

            Assert.Empty(await analyzer.AnalyzeAsync([Image(path)]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MessageAttachment Image(string path)
        => new(path, Path.GetFileName(path), "image/png", 3, AttachmentKind.Image);

    private static async Task<string> NewImageAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "shoyee-analyzer-" + Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        return path;
    }

    private sealed class FakeVisionClient(string reply) : IChatClient
    {
        public List<IReadOnlyList<AIContent>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.Single().Contents.ToArray());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingVisionClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("端点挂了");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeTranscriber(string text) : ISpeechTranscriber
    {
        public string Name => "fake";

        public bool Configured => true;

        public string? LastPath { get; private set; }

        public Task<TranscriptionResult?> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
        {
            LastPath = audioPath;
            return Task.FromResult<TranscriptionResult?>(new TranscriptionResult(text));
        }
    }
}
