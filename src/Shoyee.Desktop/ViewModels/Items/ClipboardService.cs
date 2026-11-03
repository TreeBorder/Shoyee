using Avalonia.Input.Platform;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>
/// 剪贴板入口：气泡里的「复制」命令不持控件，窗口打开时把 TopLevel 的剪贴板装进来。
/// 装不上（测试 / 无界面）时复制静默失败，界面给一句提示。
/// </summary>
public static class ClipboardService
{
    private static IClipboard? _clipboard;

    /// <summary>绑定当前窗口的剪贴板（窗口打开时调用）。</summary>
    /// <param name="clipboard">TopLevel 的剪贴板。</param>
    public static void Attach(IClipboard? clipboard) => _clipboard = clipboard;

    /// <summary>写一段文本；成功返回 true。</summary>
    /// <param name="text">要复制的文本。</param>
    public static async Task<bool> SetTextAsync(string text)
    {
        if (_clipboard is null || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            await _clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
