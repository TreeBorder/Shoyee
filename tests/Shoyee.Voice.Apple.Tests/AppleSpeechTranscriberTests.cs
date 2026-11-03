using System.Text;
using Shoyee.Voice.Apple;
using Xunit;

namespace Shoyee.Voice.Apple.Tests;

/// <summary>
/// Apple 本地转写：helper 调用、失败降级、输入校验，以及 silk 解码前的 WAV 包头。
/// helper 用临时 shell 脚本代替真的 <c>speech-stt</c>（不碰系统授权）。
/// </summary>
public sealed class AppleSpeechTranscriberTests
{
    [Theory]
    [InlineData("a.wav", true)]
    [InlineData("a.silk", true)]
    [InlineData("a.m4a", true)]
    [InlineData("a.ogg", true)]
    [InlineData("a.png", false)]
    [InlineData("a.txt", false)]
    [InlineData("a", false)]
    public void IsAudio_ClassifiesByExtension(string name, bool expected)
        => Assert.Equal(expected, AppleSpeechTranscriber.IsAudio(name));

    [Fact]
    public async Task Transcribe_ReturnsHelperStdout()
    {
        using var fixture = new HelperFixture("printf '今晚想吃火锅'");
        using var audio = TempAudio(".wav");

        var result = await fixture.Transcriber.TranscribeAsync(audio.Path);

        Assert.NotNull(result);
        Assert.Equal("今晚想吃火锅", result!.Text);
    }

    [Fact]
    public async Task Transcribe_HelperFailure_ReturnsNull()
    {
        using var fixture = new HelperFixture("echo '识别失败' >&2; exit 1");
        using var audio = TempAudio(".wav");

        Assert.Null(await fixture.Transcriber.TranscribeAsync(audio.Path));
    }

    [Fact]
    public async Task Transcribe_EmptyOutput_ReturnsNull()
    {
        using var fixture = new HelperFixture("true");
        using var audio = TempAudio(".wav");

        Assert.Null(await fixture.Transcriber.TranscribeAsync(audio.Path));
    }

    [Fact]
    public async Task Transcribe_PassesLanguageArgument()
    {
        using var fixture = new HelperFixture("printf '%s' \"$2\"", language: "en-US");
        using var audio = TempAudio(".wav");

        var result = await fixture.Transcriber.TranscribeAsync(audio.Path);

        Assert.Equal("en-US", result!.Text);
    }

    [Fact]
    public async Task Transcribe_MissingFile_Throws()
    {
        using var fixture = new HelperFixture("true");
        var path = Path.Combine(Path.GetTempPath(), $"voice-missing-{Guid.NewGuid():N}.wav");

        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Transcriber.TranscribeAsync(path));
    }

    [Fact]
    public async Task Transcribe_NonAudio_Throws()
    {
        using var fixture = new HelperFixture("true");
        using var file = TempAudio(".txt");

        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Transcriber.TranscribeAsync(file.Path));
    }

    [Fact]
    public void Configured_FalseWhenHelperMissing()
    {
        var transcriber = new AppleSpeechTranscriber(
            new AppleVoiceOptions(HelperPath: Path.Combine(Path.GetTempPath(), "no-such-helper")));

        Assert.False(transcriber.Configured);
    }

    [Fact]
    public void Wrap_WritesStandardWavHeader()
    {
        var pcm = new byte[100];

        var wav = WavPacking.Wrap(pcm, 24000);

        Assert.Equal(144, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(136, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(24000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(48000, BitConverter.ToInt32(wav, 28));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(100, BitConverter.ToInt32(wav, 40));
    }

    private static TempFile TempAudio(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"voice-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46]);
        return new TempFile(path);
    }

    /// <summary>临时 shell 脚本充当 speech-stt（stdout = stdout，$1 = 音频，$2 = 语言）。</summary>
    private sealed class HelperFixture : IDisposable
    {
        private readonly string _directory;

        public HelperFixture(string script, string language = "zh-CN")
        {
            _directory = Path.Combine(Path.GetTempPath(), $"voice-helper-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            var helper = Path.Combine(_directory, "speech-stt");
            File.WriteAllText(helper, "#!/bin/sh\n" + script + "\n");
            File.SetUnixFileMode(
                helper,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Transcriber = new AppleSpeechTranscriber(
                new AppleVoiceOptions(HelperPath: helper, Language: language));
        }

        public AppleSpeechTranscriber Transcriber { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch
            {
                // 清理失败不追究
            }
        }
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string path) => Path = path;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch
            {
                // 清理失败不追究
            }
        }
    }
}
