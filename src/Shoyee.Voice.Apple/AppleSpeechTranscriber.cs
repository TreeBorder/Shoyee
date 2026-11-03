using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SilkCodec.NET;
using Shoyee.Voice;

namespace Shoyee.Voice.Apple;

/// <summary>
/// 本地语音转写：macOS Speech 框架，经 <c>speech-stt</c> helper 子进程调用（stdout = 文本）。
///
/// 链路（移植自 Secretary，坑都带过来了）：
/// <list type="bullet">
/// <item>微信 <c>.silk</c> → <c>DrAbc.SilkCodec</c> 解码 PCM → 自包 WAV → helper；</item>
/// <item>其它音频（wav/mp3/m4a/aac…）→ 直接喂 helper；</item>
/// <item>helper 失败且不是 silk 时，有 ffmpeg 就转 16k WAV 再试一次（系统 API 不认 ogg/opus/amr）。</item>
/// </list>
///
/// ⚠️ helper 的识别回调投递在主 runloop，所以 <c>speech-stt.swift</c> 里必须 <c>dispatchMain()</c>；
/// 主线程上 <c>semaphore.wait</c> 会死锁到超时（Secretary 实测：0.7s 的活挂满 120s）。
///
/// ⚠️ 首次运行会弹系统「语音识别」授权框，授权归属是调用方（开发时是终端，打包后是 Shoyee.app）。
/// </summary>
public sealed class AppleSpeechTranscriber : ISpeechTranscriber
{
    /// <summary>可转写音频扩展名（小写含点）。</summary>
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".silk", ".wav", ".mp3", ".m4a", ".aac", ".aiff", ".aif", ".caf", ".flac", ".ogg", ".opus", ".amr",
    };

    private readonly AppleVoiceOptions _options;
    private readonly ILogger<AppleSpeechTranscriber>? _logger;
    private readonly string _helper;
    private bool _helperMissingLogged;

    /// <summary>按配置创建。</summary>
    /// <param name="options">Apple 自己的配置节。</param>
    /// <param name="logger">日志（可空，测试用）。</param>
    public AppleSpeechTranscriber(AppleVoiceOptions options, ILogger<AppleSpeechTranscriber>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
        _helper = string.IsNullOrWhiteSpace(options.HelperPath)
            ? Path.Combine(AppContext.BaseDirectory, "speech-stt")
            : options.HelperPath;
    }

    /// <inheritdoc />
    public string Name => "apple";

    /// <summary>macOS 且 helper 在位才算配置好（工具只在真正能用时挂载）。</summary>
    public bool Configured => OperatingSystem.IsMacOS() && File.Exists(_helper);

    /// <summary>文件是不是可转写音频（渠道侧先过滤用，别把图片/文档丢进来）。</summary>
    /// <param name="path">文件路径。</param>
    public static bool IsAudio(string path) => AudioFormats.HasExtension(path, AudioExtensions);

    /// <inheritdoc />
    public async Task<TranscriptionResult?> TranscribeAsync(
        string audioPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("音频文件不存在。", audioPath);
        }

        if (!IsAudio(audioPath))
        {
            throw new NotSupportedException(
                $"不支持转写 {Path.GetExtension(audioPath)}；支持：" + string.Join("、", AudioExtensions.Order()));
        }

        if (!File.Exists(_helper))
        {
            if (!_helperMissingLogged)
            {
                _helperMissingLogged = true;
                _logger?.LogWarning("speech-stt helper 不在，跳过转写：{Path}", _helper);
            }

            return null;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));
        var input = audioPath;
        string? tempWav = null;
        try
        {
            if (Path.GetExtension(audioPath).Equals(".silk", StringComparison.OrdinalIgnoreCase))
            {
                tempWav = DecodeSilkToWav(audioPath);
                if (tempWav is null)
                {
                    return null;
                }

                input = tempWav;
            }

            var text = await RunHelperAsync(_helper, input, timeout, cancellationToken).ConfigureAwait(false);

            if (text is null && tempWav is null && FindFfmpeg() is { } ffmpeg)
            {
                var converted = await ConvertToWavAsync(ffmpeg, audioPath, timeout, cancellationToken).ConfigureAwait(false);
                if (converted is not null)
                {
                    tempWav = converted;
                    text = await RunHelperAsync(_helper, converted, timeout, cancellationToken).ConfigureAwait(false);
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            _logger?.LogInformation("音频转写完成：{File} → {Chars} 字", Path.GetFileName(audioPath), text.Length);
            return new TranscriptionResult(text.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "音频转写失败（跳过）：{File}", Path.GetFileName(audioPath));
            return null;
        }
        finally
        {
            if (tempWav is not null)
            {
                TryDelete(tempWav);
            }
        }
    }

    /// <summary>.silk → PCM → WAV 临时文件；解码失败返回 null。</summary>
    private string? DecodeSilkToWav(string silkPath)
    {
        try
        {
            var decoder = new SilkDecoder(new SilkDecoderOptions { SampleRate = 24000 });
            var pcm = decoder.Decode(File.ReadAllBytes(silkPath));
            if (pcm.Length == 0)
            {
                return null;
            }

            var wavPath = Path.Combine(Path.GetTempPath(), $"shoyee-stt-{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(wavPath, WavPacking.Wrap(pcm, 24000));
            return wavPath;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "silk 解码失败：{File}", Path.GetFileName(silkPath));
            return null;
        }
    }

    /// <summary>跑 helper：stdout = 文本；非零退出/超时/空输出 → null。</summary>
    private async Task<string?> RunHelperAsync(
        string helper,
        string input,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = helper,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(input);
        psi.ArgumentList.Add(_options.Language);

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var stderr = await stderrTask.ConfigureAwait(false);
                _logger?.LogDebug("speech-stt exit {Code}: {Err}", process.ExitCode, stderr.Trim());
                return null;
            }

            return string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 已经退出
            }

            _logger?.LogWarning("speech-stt 超时（{Sec}s），放弃该文件", timeout.TotalSeconds);
            return cancellationToken.IsCancellationRequested
                ? throw new OperationCanceledException(cancellationToken)
                : null;
        }
    }

    /// <summary>ffmpeg 兜底：转 16k mono WAV 到临时文件；失败 null。</summary>
    private async Task<string?> ConvertToWavAsync(
        string ffmpeg,
        string input,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"shoyee-stt-{Guid.NewGuid():N}.wav");
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-y", "-i", input, "-ar", "16000", "-ac", "1", outPath })
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return process.ExitCode == 0 && File.Exists(outPath) ? outPath : null;
        }
        catch (OperationCanceledException)
        {
            TryDelete(outPath);
            return cancellationToken.IsCancellationRequested
                ? throw new OperationCanceledException(cancellationToken)
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>找 ffmpeg：PATH 里有就有（launchd 的 PATH 含 homebrew）。</summary>
    private static string? FindFfmpeg()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, "ffmpeg");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 临时文件清理失败不追究
        }
    }
}
