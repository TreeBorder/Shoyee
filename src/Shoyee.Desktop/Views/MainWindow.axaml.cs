using Avalonia.Controls;
using Avalonia.Input;
using Shoyee.Desktop.ViewModels;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Views;

/// <summary>主窗口：自绘标题区（40px）+ 左侧菜单/对话列表 + 右侧内容；设置是独立窗口。</summary>
public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;
    private ImportWindow? _importWindow;

    /// <summary>创建窗口。</summary>
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ClipboardService.Attach(Clipboard);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (DataContext is MainViewModel previous)
        {
            previous.SettingsRequested -= OnSettingsRequested;
            previous.ImportRequested -= OnImportRequested;
        }

        base.OnDataContextChanged(e);

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SettingsRequested += OnSettingsRequested;
            viewModel.ImportRequested += OnImportRequested;
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SettingsRequested -= OnSettingsRequested;
            viewModel.ImportRequested -= OnImportRequested;
            _ = viewModel.ShutdownAsync();
        }

        _settingsWindow?.Close();
        _importWindow?.Close();
        base.OnClosed(e);
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow { DataContext = DataContext };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    private void OnImportRequested(object? sender, EventArgs e)
    {
        if (_importWindow is { IsVisible: true })
        {
            _importWindow.Activate();
            return;
        }

        _importWindow = new ImportWindow { DataContext = DataContext };
        _importWindow.Closed += (_, _) => _importWindow = null;
        _importWindow.Show(this);
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
