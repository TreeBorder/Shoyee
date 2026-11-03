using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Shoyee.Desktop.ViewModels;

namespace Shoyee.Desktop.Views.Settings;

/// <summary>设置 · 本机 · 资料与头像。</summary>
public partial class ProfileSettingsView : UserControl
{
    /// <summary>创建视图。</summary>
    public ProfileSettingsView()
    {
        InitializeComponent();
    }

    private async void PickUserAvatar_Click(object? sender, RoutedEventArgs e) => await PickAsync(isUser: true);

    private async void PickAgentAvatar_Click(object? sender, RoutedEventArgs e) => await PickAsync(isUser: false);

    private async Task PickAsync(bool isUser)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = isUser ? "选择我的头像" : "选择智能体头像",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        var path = files.Count > 0 ? files[0].Path.LocalPath : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (isUser)
        {
            viewModel.SetUserAvatar(path);
        }
        else
        {
            viewModel.SetAgentAvatar(path);
        }
    }
}
