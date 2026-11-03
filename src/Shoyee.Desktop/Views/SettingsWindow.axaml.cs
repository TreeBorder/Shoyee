using Avalonia.Controls;
using Avalonia.Input;
using Shoyee.Desktop.ViewModels;

namespace Shoyee.Desktop.Views;

/// <summary>
/// 设置窗口：独立窗口，和主窗口共用同一个 <see cref="MainViewModel"/>，
/// 所以改外观 / 主题色会即时作用到主窗口。
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>创建设置窗口。</summary>
    public SettingsWindow()
    {
        InitializeComponent();

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SettingsCloseRequested += OnCloseRequested;
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (DataContext is MainViewModel previous)
        {
            previous.SettingsCloseRequested -= OnCloseRequested;
        }

        base.OnDataContextChanged(e);

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SettingsCloseRequested += OnCloseRequested;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SettingsCloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
