using System.Diagnostics;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>
/// 点开文件气泡：普通文档交给系统默认程序，脚本/可执行/安装包只在 Finder 里定位。
///
/// 路径来自模型写的正文（不像 <c>send_file</c> 有工作区围栏），所以不把「打开」直接
/// 交给 LaunchServices 去执行 <c>.command</c> / <c>.app</c> 这类东西——那等于让正文变成可执行入口。
/// </summary>
public static class LocalFileOpener
{
    private static readonly HashSet<string> RevealOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sh", ".command", ".app", ".pkg", ".dmg", ".scpt", ".workflow", ".terminal",
        ".jar", ".exe", ".bat", ".cmd", ".vb", ".bin",
    };

    /// <summary>路径当前是否存在（不存在时气泡显示提示、按钮禁用）。</summary>
    /// <param name="path">本地路径。</param>
    public static bool CanOpen(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && (File.Exists(path) || Directory.Exists(path));

    /// <summary>该路径是否只能用「在 Finder 中显示」，而不是直接打开。</summary>
    /// <param name="path">本地路径。</param>
    public static bool ShouldRevealOnly(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Length > 0 && RevealOnlyExtensions.Contains(extension);
    }

    /// <summary>打开或定位文件；失败静默（气泡上已有存在性提示）。</summary>
    /// <param name="path">本地绝对路径。</param>
    public static void Open(string path)
    {
        if (!CanOpen(path))
        {
            return;
        }

        try
        {
            if (!OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("open") { UseShellExecute = false },
            };
            if (ShouldRevealOnly(path))
            {
                process.StartInfo.ArgumentList.Add("-R");
            }

            process.StartInfo.ArgumentList.Add(path);
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 打不开就让用户自己处理，不弹窗打断对话
        }
    }
}
