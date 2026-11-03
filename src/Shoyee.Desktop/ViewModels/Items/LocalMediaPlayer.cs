using System.Diagnostics;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>
/// 本地音频播放：macOS 用 <c>afplay</c>（支持 mp3 / wav / m4a / aiff / flac 等）。
/// 同一时间只播一条，新的播放会先停掉上一条。
/// </summary>
public static class LocalMediaPlayer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".aac", ".aiff", ".aif", ".flac", ".ogg", ".caf",
    };

    private static readonly object Gate = new();
    private static Process? _current;

    /// <summary>该文件扩展名能否用系统播放器播放。</summary>
    /// <param name="path">本地路径。</param>
    public static bool IsSupported(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>播放一个本地音频文件；播完返回。</summary>
    /// <param name="path">本地绝对路径。</param>
    /// <param name="cancellationToken">取消令牌（取消时停止播放）。</param>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="NotSupportedException">格式不在支持列表里（例如微信语音的 .silk）。</exception>
    public static async Task PlayAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("音频文件不存在", path);
        }

        if (!IsSupported(path))
        {
            throw new NotSupportedException(
                $"系统播放器不支持 {Path.GetExtension(path)} 格式（微信语音 .silk 需要额外转码）");
        }

        if (!OperatingSystem.IsMacOS())
        {
            LocalFileOpener.Open(path);
            return;
        }

        Stop();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("afplay") { UseShellExecute = false },
        };
        process.StartInfo.ArgumentList.Add(path);
        process.Start();
        lock (Gate)
        {
            _current = process;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            lock (Gate)
            {
                if (ReferenceEquals(_current, process))
                {
                    _current = null;
                }
            }
        }
    }

    /// <summary>停掉当前播放（没有在播时什么也不做）。</summary>
    public static void Stop()
    {
        Process? process;
        lock (Gate)
        {
            process = _current;
            _current = null;
        }

        if (process is not null)
        {
            TryKill(process);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 进程已经退出或没有权限：不用打断界面
        }
    }
}
