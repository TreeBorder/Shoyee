using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Shoyee.Desktop.ViewModels;

namespace Shoyee.Desktop.Views;

/// <summary>会话导入页：两个路径输入 + 两个导入按钮。</summary>
public partial class ImportView : UserControl
{
    /// <summary>创建视图。</summary>
    public ImportView()
    {
        InitializeComponent();
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = await PickFolderAsync($"选择 {viewModel.Import.SelectedSource.Title} 的数据目录");
        if (path is not null)
        {
            viewModel.Import.SelectedSource.Path = path;
        }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
