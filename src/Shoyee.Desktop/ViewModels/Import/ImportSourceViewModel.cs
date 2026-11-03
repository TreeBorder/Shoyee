using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.ViewModels.Import;

/// <summary>一个导入来源的独立页面：路径、导入按钮、状态和对话预览。</summary>
public partial class ImportSourceViewModel : ViewModelBase
{
    private readonly Func<string, IReadOnlyList<SessionItem>>? _workImport;
    private readonly Func<string, RoleplayContent>? _roleplayImport;
    private readonly Action<IReadOnlyList<SessionItem>> _applyWork;
    private readonly Action<RoleplayContent> _applyRoleplay;
    private IReadOnlyList<SessionItem>? _workResult;
    private RoleplayContent? _roleplayResult;

    internal ImportSourceViewModel(
        ImportSourceDescriptor descriptor,
        Action<IReadOnlyList<SessionItem>> applyWork,
        Action<RoleplayContent> applyRoleplay)
    {
        Id = descriptor.Id;
        Mode = descriptor.Mode;
        Title = descriptor.Title;
        Description = descriptor.Description;
        Placeholder = descriptor.Placeholder;
        _workImport = descriptor.WorkImport;
        _roleplayImport = descriptor.RoleplayImport;
        _applyWork = applyWork;
        _applyRoleplay = applyRoleplay;
    }

    /// <summary>稳定 id（写进 desktop.json）。</summary>
    public string Id { get; }

    /// <summary>导入到哪种模式。</summary>
    public ImportMode Mode { get; }

    /// <summary>来源名。</summary>
    public string Title { get; }

    /// <summary>路径说明。</summary>
    public string Description { get; }

    /// <summary>输入框示例。</summary>
    public string Placeholder { get; }

    /// <summary>是否工作模式来源。</summary>
    public bool IsWork => Mode == ImportMode.Work;

    /// <summary>是否角色模式来源。</summary>
    public bool IsRoleplay => Mode == ImportMode.Roleplay;

    /// <summary>模式标签。</summary>
    public string ModeLabel => IsWork ? "工作模式" : "角色模式";

    /// <summary>预览区标题：工作模式是会话预览，角色模式是消息预览。</summary>
    public string PreviewLabel => IsWork ? "会话预览" : "消息预览";

    /// <summary>应用按钮文案。</summary>
    public string ApplyLabel => IsWork ? "应用到工作模式" : "应用到角色模式";

    /// <summary>左侧来源是否当前选中。</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>路径。</summary>
    [ObservableProperty]
    public partial string Path { get; set; } = "";

    /// <summary>导入状态。</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "未导入";

    /// <summary>是否正在读取。</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>是否已有可预览、可应用的数据。</summary>
    [ObservableProperty]
    public partial bool HasPreview { get; set; }

    /// <summary>预览标题。</summary>
    [ObservableProperty]
    public partial string PreviewTitle { get; set; } = "";

    /// <summary>工作模式预览里选中的会话。</summary>
    [ObservableProperty]
    public partial SessionItem? SelectedPreviewSession { get; set; }

    /// <summary>工作模式预览的会话列表。</summary>
    public ObservableCollection<SessionItem> PreviewSessions { get; } = [];

    /// <summary>当前预览的消息（工作模式随会话切换，角色模式是整条时间线）。</summary>
    public ObservableCollection<ChatMessageItem> PreviewMessages { get; } = [];

    /// <summary>请求父级把自己设为当前来源。</summary>
    internal Action<ImportSourceViewModel>? SelectRequested { get; set; }

    /// <summary>路径或导入结果变化时通知父级保存。</summary>
    internal Action? Changed { get; set; }

    /// <summary>选中这个来源。</summary>
    [RelayCommand]
    private void Select() => SelectRequested?.Invoke(this);

    /// <summary>读取数据并填入预览，不改主界面。</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = "正在读取…";
        var path = Path;
        try
        {
            if (_workImport is not null)
            {
                var sessions = await Task.Run(() => _workImport(path));
                _workResult = sessions;
                _roleplayResult = null;

                PreviewSessions.Clear();
                PreviewMessages.Clear();
                foreach (var session in sessions)
                {
                    PreviewSessions.Add(session);
                }

                SelectedPreviewSession = sessions.FirstOrDefault();
                PreviewTitle =
                    $"{sessions.Count} 个会话 · {sessions.Sum(session => session.Messages.Count)} 条消息";
                HasPreview = sessions.Count > 0;
                Status = HasPreview ? "读取完成，确认预览后应用" : "没有找到可导入的会话";
            }
            else if (_roleplayImport is not null)
            {
                var content = await Task.Run(() => _roleplayImport(path));
                _roleplayResult = content;
                _workResult = null;
                PreviewSessions.Clear();
                SelectedPreviewSession = null;

                PreviewMessages.Clear();
                foreach (var message in content.Messages)
                {
                    PreviewMessages.Add(message);
                }

                PreviewTitle =
                    $"{content.CharacterName} · {content.Messages.Count} 条消息";
                HasPreview = content.Messages.Count > 0;
                Status = HasPreview ? "读取完成，确认预览后应用" : "没有找到可导入的对话";
            }
        }
        catch (Exception ex)
        {
            _workResult = null;
            _roleplayResult = null;
            HasPreview = false;
            PreviewSessions.Clear();
            PreviewMessages.Clear();
            Status = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>把当前预览写进工作 / 角色模式。</summary>
    [RelayCommand]
    private void Apply()
    {
        if (_workResult is not null)
        {
            _applyWork(_workResult);
            Status = $"已应用到工作模式 · {_workResult.Count} 个会话";
        }
        else if (_roleplayResult is not null)
        {
            _applyRoleplay(_roleplayResult);
            Status = $"已应用到角色模式 · {_roleplayResult.Messages.Count} 条消息";
        }
    }

    partial void OnPathChanged(string value) => Changed?.Invoke();

    partial void OnSelectedPreviewSessionChanged(SessionItem? value)
    {
        if (!IsWork)
        {
            return;
        }

        PreviewMessages.Clear();
        if (value is not null)
        {
            foreach (var message in value.Messages)
            {
                PreviewMessages.Add(message);
            }
        }
    }
}
