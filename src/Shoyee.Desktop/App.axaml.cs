using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Shoyee.Client.Transport;
using Shoyee.Desktop.Theming;
using Shoyee.Desktop.ViewModels;
using Shoyee.Desktop.Views;

namespace Shoyee.Desktop;

public partial class App : Application
{
    /// <summary>macOS 的进程名/菜单名必须在平台初始化前确定，运行时再改不会生效。</summary>
    public App()
    {
        Name = "筱义";
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new AppearanceService(ShoyeePaths.Default.AppDataRoot)),
            };

            // 常驻 bundle 是 LSUIElement；桌面窗口打开时才显示 Dock 图标
            Platform.MacDock.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
