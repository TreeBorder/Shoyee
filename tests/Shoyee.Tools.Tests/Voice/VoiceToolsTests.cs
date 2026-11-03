using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Voice;
using Shoyee.Voice;
using Xunit;

namespace Shoyee.Tools.Tests.Voice;

/// <summary>语音工具面：按方向挂载、落盘位置、错误信息（provider 用假实现，不连网）。</summary>
public sealed class VoiceToolsTests
{
    [Fact]
    public void NoProviders_ProduceNoTools()
    {
        Assert.Empty(VoiceTools.Create(Config(), null, null));
    }

    [Fact]
    public void OnlyTranscriber_HidesSynthesisTools()
    {
        var names = Names(VoiceTools.Create(Config(), null, new FakeTranscriber()));

        Assert.Equal([VoiceTools.TranscribeToolName], names);
    }

    [Fact]
    public void BothDirections_ProduceAllThreeTools()
    {
        var names = Names(VoiceTools.Create(Config(), new FakeSynthesizer(), new FakeTranscriber()));

        Assert.Equal(
            [VoiceTools.SpeakToolName, VoiceTools.HelpToolName, VoiceTools.TranscribeToolName],
            names);
    }

    [Fact]
    public void UnconfiguredProvider_IsSkipped()
    {
        Assert.Empty(VoiceTools.Create(Config(), new FakeSynthesizer(configured: false), null));
    }

    [Fact]
    public void Tools_AreMountedInBothProfiles()
    {
        var builder = new ToolsetBuilder(
            Config(),
            new ToolsetServices(Synthesizer: new FakeSynthesizer(), Transcriber: new FakeTranscriber()));

        foreach (var profile in new[] { ToolProfile.Work, ToolProfile.Roleplay })
        {
            var names = builder.Build(profile).Tools.OfType<AIFunction>().Select(static f => f.Name).ToList();
            Assert.Contains(VoiceTools.SpeakToolName, names);
            Assert.Contains(VoiceTools.TranscribeToolName, names);
        }
    }

    [Fact]
    public async Task Speak_WritesAudioUnderWorkspaceAssets()
    {
        var root = NewRoot();
        try
        {
            var synthesizer = new FakeSynthesizer();
            var tool = Tool(VoiceTools.Create(Config(root), synthesizer, null), VoiceTools.SpeakToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments { ["text"] = "晚上好", ["style"] = "温柔" });

            var path = ExtractPath(result?.ToString());
            Assert.True(File.Exists(path), $"没有落盘：{path}");
            Assert.StartsWith(Path.Combine(root, "assets", "audio"), path);
            Assert.Equal(FakeSynthesizer.Audio, await File.ReadAllBytesAsync(path));
            Assert.Equal("晚上好", synthesizer.LastRequest!.Text);
            Assert.Equal("温柔", synthesizer.LastRequest.Style);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Speak_RegistersOutboundAttachment()
    {
        var root = NewRoot();
        try
        {
            var collector = new Shoyee.Tools.Media.OutboundAttachments();
            Shoyee.Tools.Media.OutboundScope.Current = collector;
            try
            {
                var tool = Tool(VoiceTools.Create(Config(root), new FakeSynthesizer(), null), VoiceTools.SpeakToolName);
                var result = await tool.InvokeAsync(new AIFunctionArguments { ["text"] = "晚上好" });

                var attachment = Assert.Single(collector.Items);
                Assert.Equal(ExtractPath(result?.ToString()), attachment.Path);
                Assert.Equal("audio/wav", attachment.Mime);
                Assert.Equal(Shoyee.Contracts.Domain.AttachmentKind.Audio, attachment.Kind);
            }
            finally
            {
                Shoyee.Tools.Media.OutboundScope.Current = null;
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Speak_EmptyText_DoesNotCallProvider()
    {
        var synthesizer = new FakeSynthesizer();
        var tool = Tool(VoiceTools.Create(Config(), synthesizer, null), VoiceTools.SpeakToolName);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["text"] = "  " });

        Assert.Contains("文本为空", result?.ToString());
        Assert.Null(synthesizer.LastRequest);
    }

    [Fact]
    public async Task Speak_OverLimit_Rejects()
    {
        var synthesizer = new FakeSynthesizer();
        var tool = Tool(VoiceTools.Create(Config(), synthesizer, null), VoiceTools.SpeakToolName);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["text"] = new string('字', 2_001) });

        Assert.Contains("分段", result?.ToString());
        Assert.Null(synthesizer.LastRequest);
    }

    [Fact]
    public async Task Speak_ProviderFailure_ReturnsMessage()
    {
        var tool = Tool(
            VoiceTools.Create(Config(), new FakeSynthesizer(failure: "余额不足"), null),
            VoiceTools.SpeakToolName);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["text"] = "你好" });

        Assert.Contains("语音合成失败：余额不足", result?.ToString());
    }

    [Fact]
    public async Task Help_ReturnsSynthesizerGuide()
    {
        var tool = Tool(
            VoiceTools.Create(Config(), new FakeSynthesizer(), null),
            VoiceTools.HelpToolName);

        var result = await tool.InvokeAsync(new AIFunctionArguments());

        Assert.Equal(FakeSynthesizer.Guide, result?.ToString());
    }

    [Fact]
    public async Task Transcribe_RelativePath_ResolvesAgainstWorkspace()
    {
        var root = NewRoot();
        try
        {
            var audio = Path.Combine(root, "inbox", "a.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(audio)!);
            await File.WriteAllBytesAsync(audio, [1, 2, 3]);

            var transcriber = new FakeTranscriber();
            var tool = Tool(VoiceTools.Create(Config(root), null, transcriber), VoiceTools.TranscribeToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = "inbox/a.wav" });

            Assert.Equal("转写好的文字", result?.ToString());
            Assert.Equal(audio, transcriber.LastPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Transcribe_MissingFile_Explains()
    {
        var tool = Tool(VoiceTools.Create(Config(), null, new FakeTranscriber()), VoiceTools.TranscribeToolName);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = "nope.wav" });

        Assert.Contains("文件不存在", result?.ToString());
    }

    [Fact]
    public async Task Transcribe_NoContent_Explains()
    {
        var root = NewRoot();
        try
        {
            var audio = Path.Combine(root, "a.wav");
            await File.WriteAllBytesAsync(audio, [1, 2, 3]);
            var tool = Tool(
                VoiceTools.Create(Config(root), null, new FakeTranscriber(nullResult: true)),
                VoiceTools.TranscribeToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = audio });

            Assert.Contains("识别不出内容", result?.ToString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static AIFunction Tool(IReadOnlyList<ToolDescriptor> descriptors, string name)
        => (AIFunction)descriptors.Single(descriptor => descriptor.Metadata.Name == name).Tool;

    private static List<string> Names(IReadOnlyList<ToolDescriptor> descriptors)
        => [.. descriptors.Select(static descriptor => descriptor.Metadata.Name)];

    private static string ExtractPath(string? text)
    {
        Assert.NotNull(text);
        var index = text!.LastIndexOf('：');
        Assert.True(index > 0, text);
        return text[(index + 1)..].Trim();
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "shoyee-voice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // 清理失败不追究
        }
    }

    private static WorkspaceConfig Config(string? root = null)
        => new(
            root ?? Path.Combine(Path.GetTempPath(), "shoyee-voice-" + Guid.NewGuid().ToString("N")),
            new ModelProfiles(
                new ModelEndpoint("http://localhost", "m", "KEY"),
                new ModelEndpoint("http://localhost", "s", "KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());

    private sealed class FakeSynthesizer(bool configured = true, string? failure = null) : ISpeechSynthesizer
    {
        public static readonly byte[] Audio = [0x52, 0x49, 0x46, 0x46, 1, 2, 3];

        public const string Guide = "音色手册正文";

        public string Name => "fake";

        public bool Configured => configured;

        public string VoiceGuide => Guide;

        public SynthesisRequest? LastRequest { get; private set; }

        public Task<SynthesizedAudio> SynthesizeAsync(
            SynthesisRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (failure is not null)
            {
                throw new InvalidOperationException(failure);
            }

            return Task.FromResult(new SynthesizedAudio(Audio, "audio/wav", ".wav"));
        }
    }

    private sealed class FakeTranscriber(TranscriptionResult? result = null, bool nullResult = false) : ISpeechTranscriber
    {
        private readonly TranscriptionResult? _result = nullResult ? null : result ?? new TranscriptionResult("转写好的文字");

        public string Name => "fake";

        public bool Configured => true;

        public string? LastPath { get; private set; }

        public Task<TranscriptionResult?> TranscribeAsync(
            string audioPath,
            CancellationToken cancellationToken = default)
        {
            LastPath = audioPath;
            return Task.FromResult(_result);
        }
    }
}
