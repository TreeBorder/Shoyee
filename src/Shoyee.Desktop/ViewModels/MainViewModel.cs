using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Client.Transport;
using Shoyee.Desktop.Rpc;
using Shoyee.Desktop.Theming;
using Shoyee.Desktop.ViewModels.Cron;
using Shoyee.Desktop.ViewModels.Import;
using Shoyee.Desktop.ViewModels.Items;
using Shoyee.Desktop.ViewModels.Settings;

namespace Shoyee.Desktop.ViewModels;

/// <summary>
/// 主窗口：左侧导航 + 对话列表；角色模式是独立全屏页；设置是独立页面（带子导航）。
///
/// 对话内容优先走 daemon 的 RPC 事件流（工作台 + 角色模式两条连接）；
/// 「会话导入」窗口仍可手动读 Secretary / Harem 的数据目录。
/// 桌面偏好（颜色模式 / 强调色 / 会话宽度 / 显示名 / 工作区路径）会写进 <c>desktop.json</c>。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly AppearanceService? _appearance;
    private readonly DaemonConnection? _daemon;
    private readonly IWorkChatGateway? _gateway;
    private readonly IRoleplayChatGateway? _roleplayGateway;
    private readonly ICronGateway? _cronGateway;
    private readonly ISettingsGateway? _settingsGateway;
    private readonly Dictionary<SettingsSection, SettingsPageViewModel> _lastPageBySection = [];
    private bool _syncingWidth;
    private bool _syncingChatOptions;
    private bool _syncingSettingsSection;
    private bool _loadingSettings;
    private bool _workAttached;
    private bool _roleplayAttached;
    private bool _cronAttached;
    private bool _settingsAttached;
    private SettingsPageViewModel? _editingPage;

    /// <summary>创建无持久化外观的预览模型（测试/设计器用）。</summary>
    public MainViewModel()
        : this(null)
    {
    }

    /// <summary>创建主窗口模型。</summary>
    /// <param name="appearance">桌面设置持久化；null = 只在内存里生效。</param>
    public MainViewModel(AppearanceService? appearance)
    {
        _appearance = appearance;
        Work = new WorkViewModel();
        Roleplay = new RoleplayViewModel();
        Cron = new CronViewModel();
        Runtime = new SettingsRuntimeViewModel();
        ModelCatalog = new ModelCatalogViewModel();
        ChannelAccounts = new ChannelAccountsViewModel();
        NotifyTargets = new NotifyTargetsViewModel();
        Import = new ImportViewModel(Work.ReplaceSessions, Roleplay.ReplaceContent);
        Import.PathsChanged += (_, _) => SaveAppearance();

        SettingsSections = SettingsCatalog.Sections
            .Select(definition => new SettingsSectionViewModel(definition, OpenSettingsSectionItem, OpenSettingsPageItem))
            .ToList();
        Runtime.Loaded += (_, _) =>
        {
            SettingsLiveConfig.Apply(SettingsSections.SelectMany(section => section.Pages), Runtime);
            OnPropertyChanged(nameof(SelectedPromptPreview));
        };
        SelectedSectionItem = SettingsSections[0];
        SelectedPageItem = SelectedSectionItem.Pages[0];

        var settings = appearance?.Load() ?? new AppearanceSettings();
        ThemePreference = settings.Theme;
        AccentPreference = settings.Accent;
        DisplayName = settings.DisplayName;
        Tagline = settings.Tagline;
        WorkspacePath = settings.WorkspacePath;
        IsSidebarCollapsed = settings.SidebarCollapsed;
        UserAvatarPath = settings.UserAvatarPath;
        AgentAvatarPath = settings.AgentAvatarPath;
        _loadingSettings = true;
        Import.LoadPaths(settings.ImportPaths);
        Work.IsFullWidth = settings.ChatFillWidth;
        Roleplay.IsFullWidth = settings.ChatFillWidth;
        Work.ShowToolCalls = settings.ShowToolCalls;
        Roleplay.ShowToolCalls = settings.ShowToolCalls;
        Work.ShowReasoning = settings.ShowReasoning;
        Roleplay.ShowReasoning = settings.ShowReasoning;
        Work.SendOnEnter = settings.SendOnEnter;
        Roleplay.SendOnEnter = settings.SendOnEnter;
        Work.IsWideBubbles = settings.WideBubbles;
        Roleplay.IsWideBubbles = settings.WideBubbles;
        PreviewCommand = settings.PreviewCommand;
        ModelCatalog.Apply(settings.Models);
        _loadingSettings = false;
        UpdateWorkModel();
        Roleplay.PortraitScale = settings.PortraitScale;
        ApplyAppearance();
        ApplyAvatars();

        ModelCatalog.Changed += OnModelCatalogChanged;

        Work.PropertyChanged += OnWorkPropertyChanged;
        Roleplay.PropertyChanged += OnRoleplayPropertyChanged;

        // 上次导入过的路径，启动时直接重新读一遍；没有路径就保持随包示例。
        _ = Import.AutoImportAsync();

        // 真实数据：只有带持久化服务（正式启动）时才连 daemon，预览/测试不打开 socket。
        if (appearance is not null)
        {
            _daemon = new DaemonConnection(ShoyeePaths.Default);
            _gateway = new DaemonWorkChatGateway(_daemon);
            _roleplayGateway = new DaemonRoleplayChatGateway(_daemon);
            _cronGateway = new DaemonCronGateway(_daemon);
            _settingsGateway = new DaemonSettingsGateway(_daemon);
            _daemon.StateChanged += OnDaemonStateChanged;
            _daemon.Start();
        }
    }

    /// <summary>请求打开设置窗口（由主窗口订阅并弹窗）。</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>请求打开会话导入窗口（由主窗口订阅并弹窗）。</summary>
    public event EventHandler? ImportRequested;

    /// <summary>请求关闭设置窗口。</summary>
    public event EventHandler? SettingsCloseRequested;

    /// <summary>是否处于工作台（false = 角色模式全屏页）。</summary>
    [ObservableProperty]
    public partial bool IsWorkMode { get; set; } = true;

    /// <summary>是否处于角色模式。</summary>
    public bool IsRoleplayMode => !IsWorkMode;

    /// <summary>当前左侧导航页。</summary>
    [ObservableProperty]
    public partial AppSection SelectedSection { get; set; } = AppSection.Chat;

    /// <summary>设置页的一级菜单（本机 / 提示词 / 角色 / 工作 / 工具 / MCP / 供应商 / 配置文件）。</summary>
    public IReadOnlyList<SettingsSectionViewModel> SettingsSections { get; }

    /// <summary>设置页当前选中的一级菜单。</summary>
    [ObservableProperty]
    public partial SettingsSectionViewModel SelectedSectionItem { get; set; } = null!;

    /// <summary>设置页当前选中的子页。</summary>
    [ObservableProperty]
    public partial SettingsPageViewModel SelectedPageItem { get; set; } = null!;

    /// <summary>当前一级菜单下的子页（左侧二级菜单）。</summary>
    public IReadOnlyList<SettingsPageViewModel> SettingsPages => SelectedSectionItem.Pages;

    /// <summary>当前一级菜单标题。</summary>
    public string SettingsSectionTitle => SelectedSectionItem.Title;

    /// <summary>当前一级菜单说明。</summary>
    public string SettingsSectionSummary => SelectedSectionItem.Summary;

    /// <summary>当前提示词子页的预览；非提示词页为 null。</summary>
    public PromptPreviewItem? SelectedPromptPreview => Runtime.GetPrompt(SelectedPageItem.Key);

    /// <summary>心跳的四组惊喜问候提示词（来自 prompts.json，可编辑）。</summary>
    public ObservableCollection<HeartbeatPromptGroup> HeartbeatPrompts => Runtime.HeartbeatPrompts;

    /// <summary>状态表（连上 daemon 后来自 status.get，可编辑并保存）。</summary>
    public ObservableCollection<StateEditItem> StateRows => Runtime.StateRows;

    /// <summary>资料索引（连上 daemon 后来自 index.list）。</summary>
    public ObservableCollection<IndexEntryItem> IndexRows => Runtime.IndexRows;

    /// <summary>子智能体列表（连上 daemon 后来自 tools.subagents.agents）。</summary>
    public ObservableCollection<SubagentRowItem> Subagents => Runtime.Subagents;

    /// <summary>预置指令。</summary>
    public IReadOnlyList<PresetCommandItem> PresetCommands => SettingsPlaceholderData.PresetCommands;

    /// <summary>模型配置：接入列表 + 三个场景的绑定。</summary>
    public ModelCatalogViewModel ModelCatalog { get; }

    /// <summary>渠道账号列表（占位数据）。</summary>
    public ChannelAccountsViewModel ChannelAccounts { get; }

    /// <summary>通知推送列表（占位数据）。</summary>
    public NotifyTargetsViewModel NotifyTargets { get; }

    /// <summary>颜色模式偏好。</summary>
    [ObservableProperty]
    public partial ThemeMode ThemePreference { get; set; } = ThemeMode.Dark;

    /// <summary>强调色偏好。</summary>
    [ObservableProperty]
    public partial AccentColor AccentPreference { get; set; } = AccentColor.Rose;

    /// <summary>左栏显示名（默认「筱义」，不显示工作区路径）。</summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = "筱义";

    /// <summary>左栏名字下面的一行说明。</summary>
    [ObservableProperty]
    public partial string Tagline { get; set; } = "个人助理 · 情感陪伴";

    /// <summary>工作区路径（本机配置，接 daemon 后再由 daemon 提供）。</summary>
    [ObservableProperty]
    public partial string WorkspacePath { get; set; } = "~/Shoyee";

    /// <summary>
    /// 预览命令模板；<c>{path}</c> 替换成文件绝对路径，空 = 平台默认。
    /// 「预览」等同于用这条命令打开文件（不经过 shell，按引号拆参数）。
    /// </summary>
    [ObservableProperty]
    public partial string PreviewCommand { get; set; } = "";

    /// <summary>左侧菜单是否折叠成图标栏。</summary>
    [ObservableProperty]
    public partial bool IsSidebarCollapsed { get; set; }

    /// <summary>用户头像文件路径（空 = 默认剪影）。</summary>
    [ObservableProperty]
    public partial string? UserAvatarPath { get; set; }

    /// <summary>智能体头像文件路径（空 = 内置头像）。</summary>
    [ObservableProperty]
    public partial string? AgentAvatarPath { get; set; }

    /// <summary>左栏实际显示的名字；空白时回落默认。</summary>
    public string BrandName => string.IsNullOrWhiteSpace(DisplayName) ? "筱义" : DisplayName.Trim();

    /// <summary>左栏实际显示的说明；空白时回落默认。</summary>
    public string BrandTagline => string.IsNullOrWhiteSpace(Tagline) ? "个人助理 · 情感陪伴" : Tagline.Trim();

    /// <summary>左侧栏宽度：展开 252 / 折叠 64。</summary>
    public double SidebarWidth => IsSidebarCollapsed ? 64 : 252;

    /// <summary>折叠按钮图标（折叠时朝右 = 展开，展开时朝左 = 折叠）。</summary>
    public Geometry SidebarCollapseIcon => IsSidebarCollapsed
        ? IconGeometries.ChevronRight
        : IconGeometries.ChevronLeft;

    /// <summary>折叠按钮提示。</summary>
    public string SidebarCollapseTip => IsSidebarCollapsed ? "展开侧栏" : "折叠侧栏";

    /// <summary>用户头像来源说明。</summary>
    public string UserAvatarLabel => string.IsNullOrWhiteSpace(UserAvatarPath)
        ? "默认剪影"
        : Path.GetFileName(UserAvatarPath);

    /// <summary>智能体头像来源说明。</summary>
    public string AgentAvatarLabel => string.IsNullOrWhiteSpace(AgentAvatarPath)
        ? "内置头像"
        : Path.GetFileName(AgentAvatarPath);

    /// <summary>是否暗黑模式。</summary>
    public bool IsDarkTheme => ThemePreference == ThemeMode.Dark;

    /// <summary>是否明亮模式。</summary>
    public bool IsLightTheme => ThemePreference == ThemeMode.Light;

    /// <summary>是否跟随系统。</summary>
    public bool IsSystemTheme => ThemePreference == ThemeMode.System;

    /// <summary>是否藕粉。</summary>
    public bool IsRose => AccentPreference == AccentColor.Rose;

    /// <summary>是否雾蓝。</summary>
    public bool IsBlue => AccentPreference == AccentColor.Blue;

    /// <summary>是否青绿。</summary>
    public bool IsTeal => AccentPreference == AccentColor.Teal;

    /// <summary>是否暖金。</summary>
    public bool IsAmber => AccentPreference == AccentColor.Amber;

    /// <summary>是否淡紫。</summary>
    public bool IsViolet => AccentPreference == AccentColor.Violet;

    /// <summary>是否石墨灰蓝。</summary>
    public bool IsSlate => AccentPreference == AccentColor.Slate;

    /// <summary>是否在对话页。</summary>
    public bool IsChatSection => SelectedSection == AppSection.Chat;

    /// <summary>是否在定时任务页。</summary>
    public bool IsCronSection => SelectedSection == AppSection.Cron;

    /// <summary>是否在任务看板。</summary>
    public bool IsBoardSection => SelectedSection == AppSection.Board;

    /// <summary>是否在收藏对话。</summary>
    public bool IsFavoritesSection => SelectedSection == AppSection.Favorites;

    /// <summary>是否在设置页。</summary>
    public bool IsSettingsSection => SelectedSection == AppSection.Settings;

    /// <summary>是否显示占位页（设置页不是占位页）。</summary>
    public bool IsPlaceholderSection => SelectedSection is not (AppSection.Chat or AppSection.Settings or AppSection.Cron);

    /// <summary>设置页是否停在外观与对话子页。</summary>
    public bool IsAppearanceSettings => SelectedPageItem.Key == "local.appearance";

    /// <summary>设置页是否停在资料与头像子页。</summary>
    public bool IsProfileSettings => SelectedPageItem.Key == "local.profile";

    /// <summary>设置页是否停在工作区与连接子页。</summary>
    public bool IsConnectionSettings => SelectedPageItem.Key == "local.connection";

    /// <summary>设置页是否停在提示词类子页（共用四篇 + 角色行为规范 / 世界观设定 / 工作指南）。</summary>
    public bool IsPromptSettings => SettingsPlaceholderData.Prompts
        .Any(item => item.Key == SelectedPageItem.Key);

    /// <summary>设置页是否停在资料索引子页。</summary>
    public bool IsWorkIndex => SelectedPageItem.Key == "work.index";

    /// <summary>设置页是否停在子智能体子页。</summary>
    public bool IsWorkSubagents => SelectedPageItem.Key == "work.subagents";

    /// <summary>设置页是否停在会话与上下文子页。</summary>
    public bool IsWorkContext => SelectedPageItem.Key == "work.context";

    /// <summary>设置页是否停在摘要与上下文子页。</summary>
    public bool IsRoleplayContext => SelectedPageItem.Key == "roleplay.context";

    /// <summary>设置页是否停在心跳与惊喜问候子页。</summary>
    public bool IsRoleplayHeartbeat => SelectedPageItem.Key == "roleplay.heartbeat";

    /// <summary>设置页是否停在状态与场景子页。</summary>
    public bool IsRoleplayStates => SelectedPageItem.Key == "roleplay.states";

    /// <summary>设置页是否停在工具集子页。</summary>
    public bool IsToolSettings => SelectedPageItem.Key == "tools.set";

    /// <summary>设置页是否停在 MCP 服务器子页。</summary>
    public bool IsMcpSettings => SelectedPageItem.Key == "mcp.servers";

    /// <summary>设置页是否停在「模型配置」子页。</summary>
    public bool IsModelsSettings => SelectedPageItem.Key == "providers.models";

    /// <summary>设置页是否停在「渠道账号」子页。</summary>
    public bool IsChannelSettings => SelectedPageItem.Key == "providers.channels";

    /// <summary>设置页是否停在「通知」子页。</summary>
    public bool IsNotifySettings => SelectedPageItem.Key == "providers.notify";

    /// <summary>设置页是否停在 config.json 原文页。</summary>
    public bool IsConfigFileSettings => SelectedPageItem.Key == "config.file";

    /// <summary>设置页是否停在重载 / 重启页。</summary>
    public bool IsConfigMaintenance => SelectedPageItem.Key == "config.maintenance";

    /// <summary>设置页是否停在结构化占位子页（不属于任何自定义视图）。</summary>
    public bool IsPlannedSettings => SelectedPageItem.Status == SettingsPageStatus.Planned
        && !IsConfigFileSettings
        && !IsConfigMaintenance
        && !IsRoleplayContext
        && !IsRoleplayHeartbeat
        && !IsRoleplayStates
        && !IsWorkIndex
        && !IsWorkSubagents
        && !IsWorkContext;

    /// <summary>左下角快速明暗切换按钮的图标：显示「点下去会变成什么」。</summary>
    public Geometry ThemeToggleIcon => ThemePreference switch
    {
        ThemeMode.Dark => IconGeometries.Sun,
        ThemeMode.Light => IconGeometries.Moon,
        _ => IconGeometries.Monitor,
    };

    /// <summary>左下角快速明暗切换按钮的提示。</summary>
    public string ThemeToggleTip => ThemePreference switch
    {
        ThemeMode.Dark => "切到明亮模式",
        ThemeMode.Light => "切到暗黑模式",
        _ => "跟随系统 · 点击切到相反模式",
    };

    /// <summary>占位页标题。</summary>
    public string PlaceholderTitle => SelectedSection switch
    {
        AppSection.Cron => "定时任务",
        AppSection.Board => "任务看板",
        AppSection.Favorites => "收藏对话",
        _ => "",
    };

    /// <summary>占位页图标。</summary>
    public string PlaceholderGlyph => SelectedSection switch
    {
        AppSection.Cron => "◷",
        AppSection.Board => "▤",
        AppSection.Favorites => "☆",
        _ => "·",
    };

    /// <summary>占位页说明。</summary>
    public string PlaceholderHint => SelectedSection switch
    {
        AppSection.Cron => "任务页会读 cron.list / cron.get / cron.executions；任务只能由模型用 cron 工具创建。",
        AppSection.Board => "任务看板预留 Squad 接入位：按状态分列展示同事提交的任务与产物。",
        AppSection.Favorites => "收藏 / 置顶的对话会集中在这里；先把列表项的置顶状态做出来，再接 session 侧接口。",
        _ => "",
    };

    /// <summary>工作模式视图模型。</summary>
    public WorkViewModel Work { get; }

    /// <summary>角色模式视图模型。</summary>
    public RoleplayViewModel Roleplay { get; }

    /// <summary>定时任务页视图模型。</summary>
    public CronViewModel Cron { get; }

    /// <summary>设置页的只读运行数据（配置原文 / 路径 / 工具 / MCP）。</summary>
    public SettingsRuntimeViewModel Runtime { get; }

    /// <summary>会话导入窗口的视图模型。</summary>
    public ImportViewModel Import { get; }

    /// <summary>提示词预览（占位数据）。</summary>
    public IReadOnlyList<PromptPreviewItem> PromptPreviews => SettingsPlaceholderData.Prompts;

    /// <summary>工具开启状态（连上 daemon 后来自 tools.list）。</summary>
    public ObservableCollection<ToolStatusItem> ToolStatuses => Runtime.Tools;

    /// <summary>MCP 连接（连上 daemon 后来自 mcp.status）。</summary>
    public ObservableCollection<McpServerItem> McpServers => Runtime.McpServers;

    /// <summary>连接状态文案（hello / 重连状态实时更新）。</summary>
    [ObservableProperty]
    public partial string ConnectionText { get; set; } = "未连接 daemon";

    /// <summary>当前是否连着 daemon。</summary>
    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    /// <summary>新建对话（占位）并回到对话页。</summary>
    [RelayCommand]
    private void NewChat()
    {
        Work.NewSessionCommand.Execute(null);
        SelectedSection = AppSection.Chat;
    }

    /// <summary>进入角色模式（独立全屏页）。</summary>
    [RelayCommand]
    private void ShowRoleplay()
    {
        IsWorkMode = false;
        Roleplay.EnsureLoaded();
    }

    /// <summary>回到工作台。</summary>
    [RelayCommand]
    private void ShowWork()
    {
        IsWorkMode = true;
        SelectedSection = AppSection.Chat;
    }

    /// <summary>打开定时任务页。</summary>
    [RelayCommand]
    private void OpenCron()
    {
        SelectedSection = AppSection.Cron;
        Cron.EnsureLoaded();
    }

    /// <summary>打开任务看板。</summary>
    [RelayCommand]
    private void OpenBoard() => SelectedSection = AppSection.Board;

    /// <summary>打开收藏对话。</summary>
    [RelayCommand]
    private void OpenFavorites() => SelectedSection = AppSection.Favorites;

    /// <summary>打开设置页（保留上次停留的一级 / 子页）。</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        Runtime.EnsureLoaded();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>打开会话导入窗口。</summary>
    [RelayCommand]
    private void OpenImport() => ImportRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>打开设置页的某个一级菜单（参数为 <see cref="SettingsSection"/> 名称）。</summary>
    /// <param name="section">Local / Prompts / Roleplay / Work / Tools / Mcp / Providers / Config。</param>
    [RelayCommand]
    private void OpenSettingsSection(string? section)
    {
        if (Enum.TryParse<SettingsSection>(section, ignoreCase: true, out var parsed))
        {
            var item = SettingsSections.FirstOrDefault(candidate => candidate.Section == parsed);
            if (item is not null)
            {
                SelectedSectionItem = item;
            }
        }

        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>打开设置页的某个子页（参数为子页键，如 <c>local.profile</c>）。</summary>
    /// <param name="key">子页键；找不到时停在当前页。</param>
    [RelayCommand]
    private void OpenSettingsPage(string? key)
    {
        var page = SettingsCatalog.FindPage(key);
        if (page is not null)
        {
            var section = SettingsSections.First(candidate => candidate.Pages.Any(p => p.Key == page.Key));
            SelectedSectionItem = section;
            SelectedPageItem = section.Pages.First(p => p.Key == page.Key);
        }

        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>进入当前设置页的编辑态（只有带配置路径的字段可改）。</summary>
    [RelayCommand]
    private void EditSettingsPage() => SelectedPageItem.BeginEdit();

    /// <summary>取消编辑并丢弃未保存的值。</summary>
    [RelayCommand]
    private void CancelEditSettingsPage() => SelectedPageItem.CancelEdit();

    /// <summary>保存当前页改过的字段（config.set 局部 patch）。</summary>
    [RelayCommand]
    private async Task SaveSettingsPageAsync()
        => await Runtime.SaveSettingsPageAsync(SelectedPageItem).ConfigureAwait(true);

    private void OpenSettingsSectionItem(SettingsSectionViewModel section)
    {
        SelectedSectionItem = section;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenSettingsPageItem(SettingsPageViewModel page)
    {
        var section = SettingsSections.First(candidate => candidate.Pages.Contains(page));
        SelectedSectionItem = section;
        SelectedPageItem = page;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>关闭设置窗口（设置页是独立窗口）。</summary>
    [RelayCommand]
    private void BackToChat() => SettingsCloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>折叠 / 展开左侧菜单。</summary>
    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    /// <summary>选择用户头像文件；会复制到应用数据目录，避免原图移动后失效。</summary>
    /// <param name="path">图片绝对路径。</param>
    public void SetUserAvatar(string path) => UserAvatarPath = ImportAvatar(path, "user") ?? UserAvatarPath;

    /// <summary>选择智能体头像文件。</summary>
    /// <param name="path">图片绝对路径。</param>
    public void SetAgentAvatar(string path) => AgentAvatarPath = ImportAvatar(path, "agent") ?? AgentAvatarPath;

    /// <summary>恢复用户默认头像。</summary>
    [RelayCommand]
    private void ResetUserAvatar() => UserAvatarPath = null;

    /// <summary>恢复智能体内置头像。</summary>
    [RelayCommand]
    private void ResetAgentAvatar() => AgentAvatarPath = null;

    /// <summary>在暗黑 / 明亮之间快速切换；跟随系统时按当前实际明暗切到相反一档。</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        var isDark = ThemePreference switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => Application.Current?.ActualThemeVariant != ThemeVariant.Light,
        };

        ThemePreference = isDark ? ThemeMode.Light : ThemeMode.Dark;
    }

    /// <summary>设置颜色模式（参数为 <see cref="ThemeMode"/> 名称）。</summary>
    /// <param name="mode">Dark / Light / System。</param>
    [RelayCommand]
    private void SetTheme(string? mode)
    {
        if (Enum.TryParse<ThemeMode>(mode, ignoreCase: true, out var parsed))
        {
            ThemePreference = parsed;
        }
    }

    /// <summary>设置强调色（参数为 <see cref="AccentColor"/> 名称）。</summary>
    /// <param name="accent">Rose / Blue / Teal / Amber / Violet / Slate。</param>
    [RelayCommand]
    private void SetAccent(string? accent)
    {
        if (Enum.TryParse<AccentColor>(accent, ignoreCase: true, out var parsed))
        {
            AccentPreference = parsed;
        }
    }

    partial void OnIsWorkModeChanged(bool value) => OnPropertyChanged(nameof(IsRoleplayMode));

    partial void OnSelectedSectionChanged(AppSection value)
    {
        OnPropertyChanged(nameof(IsChatSection));
        OnPropertyChanged(nameof(IsCronSection));
        OnPropertyChanged(nameof(IsBoardSection));
        OnPropertyChanged(nameof(IsFavoritesSection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(IsPlaceholderSection));
        OnPropertyChanged(nameof(PlaceholderTitle));
        OnPropertyChanged(nameof(PlaceholderHint));
        OnPropertyChanged(nameof(PlaceholderGlyph));
    }

    partial void OnSelectedSectionItemChanged(SettingsSectionViewModel value)
    {
        if (value is null)
        {
            return;
        }

        foreach (var section in SettingsSections)
        {
            section.IsActive = ReferenceEquals(section, value);
            section.IsExpanded = ReferenceEquals(section, value);
        }

        OnPropertyChanged(nameof(SettingsPages));
        OnPropertyChanged(nameof(SettingsSectionTitle));
        OnPropertyChanged(nameof(SettingsSectionSummary));

        var target = _lastPageBySection.TryGetValue(value.Section, out var remembered)
            && value.Pages.Contains(remembered)
                ? remembered
                : value.Pages[0];

        _syncingSettingsSection = true;
        SelectedPageItem = target;
        _syncingSettingsSection = false;
    }

    partial void OnSelectedPageItemChanged(SettingsPageViewModel value)
    {
        if (value is null)
        {
            return;
        }

        if (!ReferenceEquals(_editingPage, value))
        {
            _editingPage?.CancelEdit();
            _editingPage = value;
        }

        if (!_syncingSettingsSection && SelectedSectionItem.Pages.Contains(value))
        {
            _lastPageBySection[SelectedSectionItem.Section] = value;
        }

        foreach (var section in SettingsSections)
        {
            foreach (var page in section.Pages)
            {
                page.IsActive = ReferenceEquals(page, value);
            }
        }

        OnPropertyChanged(nameof(SelectedPromptPreview));
        OnPropertyChanged(nameof(IsAppearanceSettings));
        OnPropertyChanged(nameof(IsProfileSettings));
        OnPropertyChanged(nameof(IsConnectionSettings));
        OnPropertyChanged(nameof(IsPromptSettings));
        OnPropertyChanged(nameof(IsWorkIndex));
        OnPropertyChanged(nameof(IsWorkSubagents));
        OnPropertyChanged(nameof(IsWorkContext));
        OnPropertyChanged(nameof(IsRoleplayContext));
        OnPropertyChanged(nameof(IsRoleplayHeartbeat));
        OnPropertyChanged(nameof(IsRoleplayStates));
        OnPropertyChanged(nameof(IsToolSettings));
        OnPropertyChanged(nameof(IsMcpSettings));
        OnPropertyChanged(nameof(IsModelsSettings));
        OnPropertyChanged(nameof(IsChannelSettings));
        OnPropertyChanged(nameof(IsNotifySettings));
        OnPropertyChanged(nameof(IsConfigFileSettings));
        OnPropertyChanged(nameof(IsConfigMaintenance));
        OnPropertyChanged(nameof(IsPlannedSettings));
    }

    partial void OnThemePreferenceChanged(ThemeMode value)
    {
        ApplyAppearance();
        SaveAppearance();
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(ThemeToggleIcon));
        OnPropertyChanged(nameof(ThemeToggleTip));
    }

    partial void OnAccentPreferenceChanged(AccentColor value)
    {
        ApplyAppearance();
        SaveAppearance();
        OnPropertyChanged(nameof(IsRose));
        OnPropertyChanged(nameof(IsBlue));
        OnPropertyChanged(nameof(IsTeal));
        OnPropertyChanged(nameof(IsAmber));
        OnPropertyChanged(nameof(IsViolet));
        OnPropertyChanged(nameof(IsSlate));
    }

    partial void OnDisplayNameChanged(string value)
    {
        OnPropertyChanged(nameof(BrandName));
        ApplyAgentDisplayName();
        SaveAppearance();
    }

    partial void OnTaglineChanged(string value)
    {
        OnPropertyChanged(nameof(BrandTagline));
        SaveAppearance();
    }

    partial void OnWorkspacePathChanged(string value) => SaveAppearance();

    partial void OnPreviewCommandChanged(string value)
    {
        LocalFilePreview.CommandTemplate = value;
        SaveAppearance();
    }

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarWidth));
        OnPropertyChanged(nameof(SidebarCollapseIcon));
        OnPropertyChanged(nameof(SidebarCollapseTip));
        SaveAppearance();
    }

    partial void OnUserAvatarPathChanged(string? value)
    {
        OnPropertyChanged(nameof(UserAvatarLabel));
        ApplyAvatars();
        SaveAppearance();
    }

    partial void OnAgentAvatarPathChanged(string? value)
    {
        OnPropertyChanged(nameof(AgentAvatarLabel));
        ApplyAvatars();
        SaveAppearance();
    }

    /// <summary>关闭主窗口时断开 daemon 连接（后台 ping 循环随之停止）。</summary>
    public async ValueTask ShutdownAsync()
    {
        if (_daemon is null)
        {
            return;
        }

        _daemon.StateChanged -= OnDaemonStateChanged;
        await _daemon.DisposeAsync();
    }

    private void OnDaemonStateChanged(DaemonConnectionState state)
        => Dispatcher.UIThread.Post(() => ApplyConnectionState(state));

    private void ApplyConnectionState(DaemonConnectionState state)
    {
        IsConnected = state.Status == DaemonStatus.Connected;
        ConnectionText = state.Status switch
        {
            DaemonStatus.Connected when state.Error is { Length: > 0 } error => $"已连接 · 角色通道未就绪（{error}）",
            DaemonStatus.Connected => $"已连接 · {state.Workspace}",
            DaemonStatus.Connecting => "正在连接 daemon…",
            _ when state.Error is { Length: > 0 } error => $"未连接 · {error}",
            _ => "未连接 daemon",
        };

        if (state.Workspace is { Length: > 0 } workspace)
        {
            WorkspacePath = workspace;
        }

        if (IsConnected)
        {
            if (!_workAttached && _gateway is not null)
            {
                _workAttached = true;
                Work.AttachGateway(_gateway);
            }
            else
            {
                Work.NotifyConnectionChanged();
            }

            // 角色连接可能比工作连接晚就绪：挂上即可，页面在切过去时补加载。
            if (!_roleplayAttached && _roleplayGateway is not null)
            {
                _roleplayAttached = true;
                Roleplay.AttachGateway(_roleplayGateway);
            }
            else
            {
                Roleplay.EnsureLoaded();
            }

            if (!_cronAttached && _cronGateway is not null)
            {
                _cronAttached = true;
                Cron.AttachGateway(_cronGateway);
            }
            else
            {
                Cron.EnsureLoaded();
            }

            if (!_settingsAttached && _settingsGateway is not null)
            {
                _settingsAttached = true;
                Runtime.AttachGateway(_settingsGateway);
            }
            else
            {
                Runtime.EnsureLoaded();
            }
        }
        else if (_workAttached)
        {
            _workAttached = false;
            Work.DetachGateway();
            _roleplayAttached = false;
            Roleplay.DetachGateway();
            _cronAttached = false;
            Cron.DetachGateway();
            _settingsAttached = false;
            Runtime.DetachGateway();
        }
        else
        {
            Work.NotifyConnectionChanged();
            Roleplay.NotifyConnectionChanged();
            Cron.NotifyConnectionChanged();
            Runtime.NotifyConnectionChanged();
        }
    }

    private void OnWorkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkViewModel.SelectedSession) && Work.SelectedSession is not null)
        {
            SelectedSection = AppSection.Chat;
        }

        if (e.PropertyName == nameof(ConversationLayoutViewModel.IsFullWidth) && !_syncingWidth)
        {
            SyncWidth(Work.IsFullWidth, source: Work);
        }

        if (e.PropertyName is nameof(ConversationLayoutViewModel.ShowToolCalls)
            or nameof(ConversationLayoutViewModel.ShowReasoning)
            or nameof(ConversationLayoutViewModel.SendOnEnter)
            or nameof(ConversationLayoutViewModel.IsWideBubbles))
        {
            SyncChatOptions(source: Work);
        }
    }

    private void OnRoleplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConversationLayoutViewModel.IsFullWidth) && !_syncingWidth)
        {
            SyncWidth(Roleplay.IsFullWidth, source: Roleplay);
        }

        if (e.PropertyName == nameof(RoleplayViewModel.PortraitScale))
        {
            SaveAppearance();
        }

        if (e.PropertyName is nameof(ConversationLayoutViewModel.ShowToolCalls)
            or nameof(ConversationLayoutViewModel.ShowReasoning)
            or nameof(ConversationLayoutViewModel.SendOnEnter)
            or nameof(ConversationLayoutViewModel.IsWideBubbles))
        {
            SyncChatOptions(source: Roleplay);
        }
    }

    private void SyncChatOptions(object source)
    {
        if (_syncingChatOptions)
        {
            return;
        }

        _syncingChatOptions = true;
        try
        {
            if (source != Work)
            {
                Work.ShowToolCalls = Roleplay.ShowToolCalls;
                Work.ShowReasoning = Roleplay.ShowReasoning;
                Work.SendOnEnter = Roleplay.SendOnEnter;
                Work.IsWideBubbles = Roleplay.IsWideBubbles;
            }

            if (source != Roleplay)
            {
                Roleplay.ShowToolCalls = Work.ShowToolCalls;
                Roleplay.ShowReasoning = Work.ShowReasoning;
                Roleplay.SendOnEnter = Work.SendOnEnter;
                Roleplay.IsWideBubbles = Work.IsWideBubbles;
            }
        }
        finally
        {
            _syncingChatOptions = false;
        }

        SaveAppearance();
    }

    private void OnModelCatalogChanged(object? sender, EventArgs e)
    {
        UpdateWorkModel();
        SaveAppearance();
    }

    private void UpdateWorkModel()
    {
        var chat = ModelCatalog.ChatEntry?.ToSettings();
        Work.UpdateModel(
            chat?.Model ?? "deepseek-flash",
            chat?.BaseUrl ?? "https://api.deepseek.com",
            chat?.ContextLength ?? 1_000_000);
    }

    private void SyncWidth(bool fill, object source)
    {
        _syncingWidth = true;
        try
        {
            if (source != Work)
            {
                Work.IsFullWidth = fill;
            }

            if (source != Roleplay)
            {
                Roleplay.IsFullWidth = fill;
            }
        }
        finally
        {
            _syncingWidth = false;
        }

        SaveAppearance();
    }

    private AppearanceSettings CurrentAppearance => new(
        ThemePreference,
        AccentPreference,
        Work.IsFullWidth,
        Roleplay.PortraitScale,
        DisplayName,
        Tagline,
        WorkspacePath,
        IsSidebarCollapsed,
        UserAvatarPath,
        AgentAvatarPath,
        Import.ExportPaths().ToDictionary(pair => pair.Key, pair => pair.Value),
        ShowToolCalls: Work.ShowToolCalls,
        ShowReasoning: Work.ShowReasoning,
        SendOnEnter: Work.SendOnEnter,
        Models: ModelCatalog.ToSettings(),
        WideBubbles: Work.IsWideBubbles,
        PreviewCommand: PreviewCommand,
        SettingsVersion: 2);

    private void ApplyAppearance()
    {
        LocalFilePreview.CommandTemplate = PreviewCommand;

        if (_appearance is not null)
        {
            _appearance.Apply(CurrentAppearance);
        }
        else
        {
            AppearanceService.ApplyToApplication(CurrentAppearance);
        }

        ApplyAgentDisplayName();
    }

    private void ApplyAvatars() => AvatarResources.Apply(UserAvatarPath, AgentAvatarPath);

    private void ApplyAgentDisplayName()
    {
        if (Application.Current is { } app)
        {
            app.Resources["AgentDisplayName"] = BrandName;
        }
    }

    private string? ImportAvatar(string path, string slot)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        // 没有持久化服务（测试/设计器）时直接用原路径
        if (_appearance is null)
        {
            return path;
        }

        try
        {
            var directory = Path.Combine(_appearance.AppDataRoot, "avatars");
            Directory.CreateDirectory(directory);

            var extension = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            var destination = Path.Combine(directory, $"{slot}{extension}");
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.Ordinal))
            {
                File.Copy(path, destination, overwrite: true);
            }

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }

    private void SaveAppearance()
    {
        if (!_loadingSettings)
        {
            _appearance?.Save(CurrentAppearance);
        }
    }
}
