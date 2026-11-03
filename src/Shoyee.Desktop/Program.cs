using Avalonia;

namespace Shoyee.Desktop;

internal static class Program
{
    // Avalonia 初始化之前不要碰任何 Avalonia / 第三方 API。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // 设计器也会调用，别删。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .WithInterFont()
            .LogToTrace();
}
