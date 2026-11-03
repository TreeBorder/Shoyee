using System.ComponentModel;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Media;
using Shoyee.Voice;

namespace Shoyee.Tools.Voice;

/// <summary>
/// 语音工具：<c>voice_speak</c>（合成）/ <c>voice_help</c>（音色手册）/ <c>voice_transcribe</c>（转写本地音频）。
///
/// 落盘策略在工具层（provider 不碰文件系统）：合成结果写
/// <c>{workspace}/assets/audio/YYYY/MM/</c>，返回绝对路径给模型/客户端。
/// 语音文件不进库——与 IPC 契约「DB 不存二进制」一致。
///
/// 合成只挂依赖 <see cref="ISpeechSynthesizer"/> 的两个工具，转写只挂依赖
/// <see cref="ISpeechTranscriber"/> 的那个：配了哪家、哪个方向，就只出现哪些工具，不留失败的空壳。
/// </summary>
public static class VoiceTools
{
    /// <summary>合成工具名。</summary>
    public const string SpeakToolName = "voice_speak";

    /// <summary>音色手册工具名。</summary>
    public const string HelpToolName = "voice_help";

    /// <summary>转写工具名。</summary>
    public const string TranscribeToolName = "voice_transcribe";

    /// <summary>单次合成文本上限（字符）。长文分段合成，别一次把整章塞进去。</summary>
    private const int MaxSpeakChars = 2_000;

    /// <summary>构建语音工具面（没有对应 provider 的方向不出现）。</summary>
    /// <param name="config">工作区配置（落盘根）。</param>
    /// <param name="synthesizer">合成实现（可空）。</param>
    /// <param name="transcriber">转写实现（可空）。</param>
    public static IReadOnlyList<ToolDescriptor> Create(
        WorkspaceConfig config,
        ISpeechSynthesizer? synthesizer,
        ISpeechTranscriber? transcriber)
    {
        ArgumentNullException.ThrowIfNull(config);

        var descriptors = new List<ToolDescriptor>(3);
        if (synthesizer is { Configured: true } synth)
        {
            descriptors.Add(CreateSpeak(config, synth));
            descriptors.Add(CreateHelp(synth));
        }

        if (transcriber is { Configured: true } stt)
        {
            descriptors.Add(CreateTranscribe(config, stt));
        }

        return descriptors;
    }

    private static ToolDescriptor CreateSpeak(WorkspaceConfig config, ISpeechSynthesizer synthesizer)
    {
        var tool = AIFunctionFactory.Create(
            async (
                [Description("要合成的文本（必填）。可以在开头加风格标签如「(温柔)」，中间插音频标签如「[轻笑]」，语法见 voice_help。")] string text,
                [Description("自然语言风格指令（可选），如「温柔一点，语速稍慢」「压着嗓子，像在密谋」。")] string? style = null,
                [Description("音色 id（可选）；不传用配置里的默认音色，可选值见 voice_help。")] string? voice = null,
                CancellationToken cancellationToken = default) =>
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return "语音合成失败：文本为空。";
                }

                var length = text.EnumerateRunes().Count();
                if (length > MaxSpeakChars)
                {
                    return $"语音合成失败：文本 {length} 字，超过单次上限 {MaxSpeakChars} 字；请分段合成。";
                }

                try
                {
                    var audio = await synthesizer
                        .SynthesizeAsync(new SynthesisRequest(text, style, voice), cancellationToken)
                        .ConfigureAwait(false);

                    var directory = Path.Combine(
                        config.Root,
                        "assets",
                        "audio",
                        DateTime.Now.ToString("yyyy"),
                        DateTime.Now.ToString("MM"));
                    Directory.CreateDirectory(directory);

                    var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + audio.Extension);
                    await File.WriteAllBytesAsync(path, audio.Data, cancellationToken).ConfigureAwait(false);

                    // 挂到本轮回复上：桌面能直接播，IM 侧由回帖旁路传文件
                    OutboundScope.Current?.Add(AttachmentFactory.FromFile(path, audio.Mime));

                    return $"已合成语音（{audio.Mime}，{audio.Data.Length / 1024.0:F1} KB）：{path}";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "语音合成失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = SpeakToolName,
                Description = "把一段文字合成为语音文件，返回本地绝对路径。适合角色扮演时发语音；工作模式需要朗读时也能用。"
                              + "文本可用风格标签与音频标签控制语气，具体写法先看 voice_help。",
            });

        return ToolDescriptor.Create(tool, ToolScope.All, writes: true, requiresApproval: false);
    }

    private static ToolDescriptor CreateHelp(ISpeechSynthesizer synthesizer)
    {
        var tool = AIFunctionFactory.Create(
            () => synthesizer.VoiceGuide,
            new AIFunctionFactoryOptions
            {
                Name = HelpToolName,
                Description = "查看当前语音提供方的音色列表、音色设计写法与风格/音频标签语法。第一次调用 voice_speak 前先读它。",
            });

        return ToolDescriptor.Create(tool, ToolScope.All, writes: false, requiresApproval: false);
    }

    private static ToolDescriptor CreateTranscribe(WorkspaceConfig config, ISpeechTranscriber transcriber)
    {
        var tool = AIFunctionFactory.Create(
            async (
                [Description("本地音频文件路径（wav/mp3/m4a/silk 等，绝对路径或相对工作区根）。")] string path,
                CancellationToken cancellationToken = default) =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return "转写失败：路径为空。";
                }

                var full = Path.IsPathRooted(path) ? path : Path.Combine(config.Root, path);
                if (!File.Exists(full))
                {
                    return $"转写失败：文件不存在（{full}）。";
                }

                try
                {
                    var result = await transcriber.TranscribeAsync(full, cancellationToken).ConfigureAwait(false);
                    return result is null
                        ? "转写失败：识别不出内容（音频太短/太吵，或识别引擎不可用）。"
                        : result.Text;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "转写失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = TranscribeToolName,
                Description = "把本地音频文件转写成文字。用户发来语音、或需要读一段录音时用。失败会说明原因。",
            });

        return ToolDescriptor.Create(tool, ToolScope.All, writes: false, requiresApproval: false);
    }
}
