using CommunityToolkit.Mvvm.ComponentModel;
using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.ViewModels.Import;

/// <summary>
/// 会话导入窗口：按「工作模式 / 角色模式」分组，每个来源一个独立页面。
/// 页面只负责读数据和预览，点「应用」后才替换主界面。
/// </summary>
public partial class ImportViewModel : ViewModelBase
{
    private readonly Action<IReadOnlyList<SessionItem>> _applyWork;
    private readonly Action<RoleplayContent> _applyRoleplay;

    /// <summary>创建导入模型。</summary>
    /// <param name="applyWork">把工作会话写回工作模式。</param>
    /// <param name="applyRoleplay">把角色时间线写回角色模式。</param>
    public ImportViewModel(
        Action<IReadOnlyList<SessionItem>> applyWork,
        Action<RoleplayContent> applyRoleplay)
    {
        _applyWork = applyWork;
        _applyRoleplay = applyRoleplay;

        WorkSources = ImportCatalog.All
            .Where(source => source.Mode == ImportMode.Work)
            .Select(CreateSource)
            .ToList();
        RoleplaySources = ImportCatalog.All
            .Where(source => source.Mode == ImportMode.Roleplay)
            .Select(CreateSource)
            .ToList();

        SelectedSource = WorkSources[0];
        SelectedSource.IsActive = true;
    }

    /// <summary>路径变化时触发（主窗口负责写 desktop.json）。</summary>
    public event EventHandler? PathsChanged;

    /// <summary>工作模式来源。</summary>
    public IReadOnlyList<ImportSourceViewModel> WorkSources { get; }

    /// <summary>角色模式来源。</summary>
    public IReadOnlyList<ImportSourceViewModel> RoleplaySources { get; }

    /// <summary>全部来源（含两种模式）。</summary>
    public IEnumerable<ImportSourceViewModel> Sources => WorkSources.Concat(RoleplaySources);

    /// <summary>当前显示的来源页。</summary>
    [ObservableProperty]
    public partial ImportSourceViewModel SelectedSource { get; set; } = null!;

    /// <summary>把上次保存的路径填回各来源页。</summary>
    /// <param name="paths">来源 id → 路径。</param>
    public void LoadPaths(IReadOnlyDictionary<string, string>? paths)
    {
        if (paths is null)
        {
            return;
        }

        foreach (var source in Sources)
        {
            if (paths.TryGetValue(source.Id, out var path) && !string.IsNullOrWhiteSpace(path))
            {
                source.Path = path;
            }
        }
    }

    /// <summary>导出各来源当前路径，用于写 desktop.json。</summary>
    /// <returns>来源 id → 路径。</returns>
    public IReadOnlyDictionary<string, string> ExportPaths() =>
        Sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Path))
            .ToDictionary(source => source.Id, source => source.Path.Trim());

    /// <summary>启动时按上次路径读取并应用（没有路径就跳过）。</summary>
    /// <returns>导入任务。</returns>
    public async Task AutoImportAsync()
    {
        foreach (var source in Sources.Where(source => !string.IsNullOrWhiteSpace(source.Path)))
        {
            await source.ImportCommand.ExecuteAsync(null);
            if (source.HasPreview)
            {
                source.ApplyCommand.Execute(null);
            }
        }
    }

    private ImportSourceViewModel CreateSource(ImportSourceDescriptor descriptor)
    {
        var source = new ImportSourceViewModel(descriptor, _applyWork, _applyRoleplay)
        {
            SelectRequested = SelectSource,
            Changed = () => PathsChanged?.Invoke(this, EventArgs.Empty),
        };
        return source;
    }

    private void SelectSource(ImportSourceViewModel source)
    {
        foreach (var candidate in Sources)
        {
            candidate.IsActive = ReferenceEquals(candidate, source);
        }

        SelectedSource = source;
    }
}
