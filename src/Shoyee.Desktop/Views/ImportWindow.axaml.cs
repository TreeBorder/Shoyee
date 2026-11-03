using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Shoyee.Desktop.Views;

/// <summary>会话导入窗口：独立窗口，和主窗口共用同一个 <c>MainViewModel</c>。</summary>
public partial class ImportWindow : Window
{
    /// <summary>创建窗口。</summary>
    public ImportWindow()
    {
        InitializeComponent();
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
