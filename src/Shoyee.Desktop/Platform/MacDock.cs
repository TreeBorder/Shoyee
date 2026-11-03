using System.Runtime.InteropServices;

namespace Shoyee.Desktop.Platform;

/// <summary>
/// macOS Dock 可见性。
///
/// 整个 筱义.app 是 <c>LSUIElement</c>（菜单栏常驻，不占 Dock）；桌面窗口是同一个 bundle 里
/// 按需启动的普通进程。启动时把 activation policy 显式切成 <c>regular</c>，Dock 图标才出现；
/// 关掉最后一个窗口进程退出，Dock 图标随之消失——与 Tailscale 的 HideDockIcon 同效果。
/// </summary>
internal static class MacDock
{
    private const int Regular = 0;
    private const nint Yes = 1;

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern nint objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern nint sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSendInt(nint receiver, nint selector, nint value);

    /// <summary>把当前进程切到普通 App：显示 Dock 图标并激活到前台。</summary>
    public static void Show()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var app = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            objc_msgSendInt(app, sel_registerName("setActivationPolicy:"), Regular);
            objc_msgSendInt(app, sel_registerName("activateIgnoringOtherApps:"), Yes);
        }
        catch (Exception)
        {
            // 拿不到 AppKit 就让系统按 bundle 默认策略走（LSUIElement → 不显示 Dock）
        }
    }
}
