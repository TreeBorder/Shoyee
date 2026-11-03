using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Shoyee.Desktop;
using Shoyee.Desktop.Theming;
using Shoyee.Desktop.ViewModels;
using Shoyee.Desktop.ViewModels.Settings;
using Shoyee.Desktop.Views;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>
/// 桌面骨架的冒烟测试：工作 / 立绘 / 气泡 / 明亮主题都能初始化、渲染出非空画面。
///
/// 设置环境变量 <c>SHOYEE_DESKTOP_SNAPSHOT</c>=目录 可把几帧 PNG 落盘，方便肉眼看布局。
/// </summary>
[Collection("Avalonia")]
public sealed class MainWindowTests
{
    public MainWindowTests()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
    }


    [Fact]
    public void RendersWorkRoleplayAndThemes()
    {
        var viewModel = new MainViewModel();
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1200,
            Height = 800,
        };

        window.Show();
        Assert.True(window.IsVisible);

        var work = Render(window, "work-dark");
        Assert.NotNull(work);
        Assert.True(work.PixelSize.Width >= 1000);
        Assert.True(work.PixelSize.Height >= 700);

        viewModel.Work.SelectedSession = viewModel.Work.Sessions.First(s => s.Title == "语音识别链路");
        Assert.NotNull(Render(window, "work-voice"));
        viewModel.Work.SelectedSession = viewModel.Work.Sessions.First(s => s.Title == "给合并项目起名");
        Assert.NotNull(Render(window, "work-tools"));

        // 展开一条工具调用，并把思考过程打开，确认折叠两态都能画出来
        var tool = viewModel.Work.VisibleMessages.First(message => message.IsTool);
        tool.IsExpanded = true;
        viewModel.Work.ShowReasoning = true;
        Assert.NotNull(Render(window, "work-tool-expanded"));
        tool.IsExpanded = false;
        viewModel.Work.ShowReasoning = false;

        viewModel.Work.SelectedSession = viewModel.Work.Sessions[0];

        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.NotNull(Render(window, "work-sidebar-collapsed"));
        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.False(viewModel.IsSidebarCollapsed);

        viewModel.SetThemeCommand.Execute("Light");
        Assert.True(viewModel.IsLightTheme);
        var light = Render(window, "work-light");
        Assert.NotNull(light);

        viewModel.SetThemeCommand.Execute("Dark");
        viewModel.SetAccentCommand.Execute("Blue");
        Assert.True(viewModel.IsBlue);
        var blue = Render(window, "work-blue");
        Assert.NotNull(blue);
        viewModel.SetAccentCommand.Execute("Rose");

        viewModel.Work.IsRightPanelOpen = false;
        var collapsed = Render(window, "work-collapsed");
        Assert.NotNull(collapsed);
        viewModel.Work.IsRightPanelOpen = true;
        Assert.NotNull(Render(window, "work-panel-open"));

        viewModel.Work.SetCenteredWidthCommand.Execute(null);
        Assert.False(viewModel.Work.IsFullWidth);
        Assert.False(viewModel.Roleplay.IsFullWidth);
        var centered = Render(window, "work-centered");
        Assert.NotNull(centered);
        viewModel.Work.SetFullWidthCommand.Execute(null);

        viewModel.OpenCronCommand.Execute(null);
        var placeholder = Render(window, "work-cron-placeholder");
        Assert.NotNull(placeholder);
        viewModel.BackToChatCommand.Execute(null);

        var importWindow = new ImportWindow { DataContext = viewModel, Width = 1150, Height = 780 };
        importWindow.Show();
        Assert.NotNull(Render(importWindow, "import"));
        importWindow.Close();

        viewModel.OpenSettingsCommand.Execute(null);
        var settingsWindow = new SettingsWindow { DataContext = viewModel, Width = 1200, Height = 800 };
        settingsWindow.Show();
        var settings = Render(settingsWindow, "settings-appearance-dark");
        Assert.NotNull(settings);

        viewModel.OpenSettingsSectionCommand.Execute("Prompts");
        Assert.NotNull(Render(settingsWindow, "settings-prompts"));
        viewModel.OpenSettingsPageCommand.Execute("prompts.memory");
        Assert.NotNull(Render(settingsWindow, "settings-prompts-memory"));
        viewModel.OpenSettingsSectionCommand.Execute("Tools");
        Assert.NotNull(Render(settingsWindow, "settings-tools"));
        viewModel.OpenSettingsSectionCommand.Execute("Mcp");
        Assert.NotNull(Render(settingsWindow, "settings-mcp"));
        viewModel.OpenSettingsSectionCommand.Execute("Providers");
        Assert.True(viewModel.IsModelsSettings);
        Assert.NotNull(Render(settingsWindow, "settings-models"));

        // 添加模型走弹窗：渲染一帧确认弹窗与数字输入框的样式
        viewModel.ModelCatalog.AddCommand.Execute(null);
        Assert.NotNull(Render(settingsWindow, "settings-models-edit"));
        viewModel.ModelCatalog.CancelDialogCommand.Execute(null);
        Assert.False(viewModel.ModelCatalog.IsDialogOpen);

        // 渠道账号与通知：同样是列表 + 添加弹窗
        viewModel.OpenSettingsPageCommand.Execute("providers.channels");
        Assert.True(viewModel.IsChannelSettings);
        Assert.NotNull(Render(settingsWindow, "settings-channels"));
        viewModel.ChannelAccounts.AddCommand.Execute(null);
        Assert.NotNull(Render(settingsWindow, "settings-channels-edit"));
        viewModel.ChannelAccounts.CancelDialogCommand.Execute(null);

        viewModel.OpenSettingsPageCommand.Execute("providers.notify");
        Assert.True(viewModel.IsNotifySettings);
        Assert.NotNull(Render(settingsWindow, "settings-notify"));
        viewModel.NotifyTargets.AddCommand.Execute(null);
        Assert.NotNull(Render(settingsWindow, "settings-notify-edit"));
        viewModel.NotifyTargets.Editing!.Kind = NotifyKind.Email;
        Assert.NotNull(Render(settingsWindow, "settings-notify-edit-email"));
        viewModel.NotifyTargets.CancelDialogCommand.Execute(null);

        viewModel.OpenSettingsPageCommand.Execute("providers.voice");
        Assert.NotNull(Render(settingsWindow, "settings-voice"));
        viewModel.OpenSettingsPageCommand.Execute("providers.image");
        Assert.NotNull(Render(settingsWindow, "settings-image"));
        viewModel.OpenSettingsPageCommand.Execute("providers.web");
        Assert.NotNull(Render(settingsWindow, "settings-web"));
        viewModel.OpenSettingsPageCommand.Execute("providers.notes");
        Assert.NotNull(Render(settingsWindow, "settings-notes"));
        viewModel.OpenSettingsPageCommand.Execute("local.profile");
        Assert.NotNull(Render(settingsWindow, "settings-local-profile"));
        viewModel.OpenSettingsPageCommand.Execute("local.connection");
        Assert.NotNull(Render(settingsWindow, "settings-local-connection"));
        viewModel.OpenSettingsSectionCommand.Execute("Work");
        Assert.NotNull(Render(settingsWindow, "settings-work-guide"));
        viewModel.OpenSettingsPageCommand.Execute("work.index");
        Assert.NotNull(Render(settingsWindow, "settings-work-index"));
        viewModel.OpenSettingsPageCommand.Execute("work.subagents");
        Assert.NotNull(Render(settingsWindow, "settings-work-subagents"));
        viewModel.OpenSettingsPageCommand.Execute("work.context");
        Assert.NotNull(Render(settingsWindow, "settings-work-context"));
        viewModel.OpenSettingsSectionCommand.Execute("Roleplay");
        Assert.Equal("roleplay.behavior", viewModel.SelectedPageItem.Key);
        Assert.True(viewModel.IsPromptSettings);
        Assert.NotNull(Render(settingsWindow, "settings-roleplay-behavior"));
        viewModel.OpenSettingsPageCommand.Execute("roleplay.world");
        Assert.NotNull(Render(settingsWindow, "settings-roleplay-world"));
        viewModel.OpenSettingsPageCommand.Execute("roleplay.context");
        Assert.True(viewModel.IsRoleplayContext);
        Assert.NotNull(Render(settingsWindow, "settings-roleplay-context"));
        viewModel.OpenSettingsPageCommand.Execute("roleplay.heartbeat");
        Assert.True(viewModel.IsRoleplayHeartbeat);
        Assert.NotNull(Render(settingsWindow, "settings-roleplay-heartbeat"));
        settingsWindow.Height = 1500;
        Assert.NotNull(Render(settingsWindow, "settings-roleplay-heartbeat-tall"));
        settingsWindow.Height = 800;
        viewModel.OpenSettingsPageCommand.Execute("roleplay.states");
        Assert.True(viewModel.IsRoleplayStates);
        Assert.NotNull(Render(settingsWindow, "settings-roleplay-states"));
        viewModel.OpenSettingsSectionCommand.Execute("Config");
        Assert.NotNull(Render(settingsWindow, "settings-config-planned"));
        viewModel.OpenSettingsPageCommand.Execute("local.appearance");

        viewModel.ToggleThemeCommand.Execute(null);
        Assert.True(viewModel.IsLightTheme);
        var settingsLight = Render(settingsWindow, "settings-appearance-light");
        Assert.NotNull(settingsLight);

        viewModel.ToggleThemeCommand.Execute(null);
        Assert.True(viewModel.IsDarkTheme);
        settingsWindow.Close();

        viewModel.ShowRoleplayCommand.Execute(null);
        var portrait = Render(window, "roleplay-portrait");
        Assert.NotNull(portrait);

        window.Width = 760;
        window.Height = 1120;
        var vertical = Render(window, "roleplay-portrait-vertical");
        Assert.NotNull(vertical);
        window.Width = 1200;
        window.Height = 800;

        viewModel.Roleplay.PortraitScale = 0.7;
        var small = Render(window, "roleplay-portrait-small");
        Assert.NotNull(small);
        viewModel.Roleplay.PortraitScale = 1.0;

        viewModel.Roleplay.ShowImCommand.Execute(null);
        viewModel.Roleplay.ShowOtherLanes = true;
        var timeline = Render(window, "roleplay-im");
        Assert.NotNull(timeline);

        window.Close();
    }

    [Fact]
    public void RendersExpandedToolAndReasoningRows()
    {
        var viewModel = new WorkViewModel { ShowReasoning = true };
        viewModel.SelectedSession = new ViewModels.Items.SessionItem
        {
            Title = "工具折叠预览",
            Preview = "展开工具调用与思考过程",
            Time = "刚刚",
            Source = "桌面",
            MessageCount = 4,
            CreatedAt = DateTimeOffset.Now,
            Messages =
            [
                new ViewModels.Items.ChatMessageItem
                {
                    Kind = ViewModels.Items.ChatEntryKind.User,
                    Text = "帮我看看输入框这一排按钮，图标大小不统一。",
                    Time = "10:00",
                },
                new ViewModels.Items.ChatMessageItem
                {
                    Kind = ViewModels.Items.ChatEntryKind.Assistant,
                    Text = "我先读一下配置，再决定怎么改。",
                    Time = "10:00",
                },
                new ViewModels.Items.ChatMessageItem
                {
                    Kind = ViewModels.Items.ChatEntryKind.Reasoning,
                    Text = "用户要改的是桌面端的输入框这一排按钮。\n" +
                           "1. 工具调用默认折叠；\n2. 思考过程可以单独打开；\n3. 图标尺寸统一。",
                    Time = "10:00",
                    IsExpanded = true,
                },
                new ViewModels.Items.ChatMessageItem
                {
                    Kind = ViewModels.Items.ChatEntryKind.Tool,
                    ToolName = "terminal",
                    ToolCallId = "call_preview",
                    Text = "rg -n \"Button.icon\" src/Shoyee.Desktop",
                    Detail = """
                        {
                          "command": "rg -n \"Button.icon\" src/Shoyee.Desktop"
                        }
                        """,
                    Result = "Theme.axaml:134:  <Style Selector=\"Button.icon\">\n" +
                             "WorkView.axaml:58:  <Button Classes=\"icon\">\n" +
                             "RoleplayView.axaml:132:  <Button Classes=\"icon\">",
                    Time = "10:00",
                    IsExpanded = true,
                },
            ],
        };

        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = new WorkView { DataContext = viewModel },
        };
        window.Show();

        Assert.NotNull(Render(window, "work-rows-expanded"));
        viewModel.ToggleBubbleWidthCommand.Execute(null);
        Assert.NotNull(Render(window, "work-rows-wide"));

        // 只留一问一答，确认平铺时用户气泡和头像、对端头像都留够距离，且右上角收角
        viewModel.SelectedSession = new ViewModels.Items.SessionItem
        {
            Title = "平铺气泡边界",
            Preview = "用户 / 助手各一条",
            Time = "刚刚",
            Source = "桌面",
            CreatedAt = DateTimeOffset.Now,
            Messages =
            [
                new ViewModels.Items.ChatMessageItem
                {
                    Kind = ViewModels.Items.ChatEntryKind.User,
                    Text = "这条消息用来看平铺时离左边助手头像区域有多远。",
                    Time = "10:00",
                },
                new ViewModels.Items.ChatMessageItem
                {
                    Kind = ViewModels.Items.ChatEntryKind.Assistant,
                    Text = "这条回答看右侧用户头像区域。",
                    Time = "10:01",
                },
            ],
        };
        Assert.NotNull(Render(window, "work-wide-user"));

        viewModel.SelectedSession = new ViewModels.Items.SessionItem
        {
            Title = "上下文闪电",
            Preview = "≥600k",
            Time = "刚刚",
            Source = "桌面",
            CreatedAt = DateTimeOffset.Now,
            Messages =
            [
                new ViewModels.Items.ChatMessageItem
                {
                    Kind = ViewModels.Items.ChatEntryKind.Tool,
                    ToolName = "terminal",
                    Text = "stress",
                    Detail = new string('a', 2_400_000),
                },
            ],
        };
        Assert.NotNull(Render(window, "work-context-electric"));
        window.Close();
    }

    [Fact]
    public void SettingsSectionsAndProfile()
    {
        var viewModel = new MainViewModel();

        Assert.Equal("筱义", viewModel.BrandName);
        Assert.Equal("个人助理 · 情感陪伴", viewModel.BrandTagline);

        viewModel.DisplayName = "  ";
        viewModel.Tagline = "自定义说明";
        Assert.Equal("筱义", viewModel.BrandName);
        Assert.Equal("自定义说明", viewModel.BrandTagline);

        Assert.False(viewModel.IsSidebarCollapsed);
        Assert.Equal(252, viewModel.SidebarWidth);
        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.True(viewModel.IsSidebarCollapsed);
        Assert.Equal(64, viewModel.SidebarWidth);
        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.False(viewModel.IsSidebarCollapsed);

        // 设置是独立窗口：命令只负责定位页面并发出打开请求
        var requested = 0;
        var closed = 0;
        viewModel.SettingsRequested += (_, _) => requested++;
        viewModel.SettingsCloseRequested += (_, _) => closed++;

        viewModel.OpenSettingsSectionCommand.Execute("Prompts");
        Assert.Equal(1, requested);
        Assert.True(viewModel.IsPromptSettings);
        Assert.False(viewModel.IsAppearanceSettings);
        Assert.Equal("prompts.agents", viewModel.SelectedPageItem.Key);
        Assert.Equal("通用规则", viewModel.SelectedPageItem.Title);
        Assert.Equal("AGENTS.shared.md", Path.GetFileName(viewModel.SelectedPromptPreview?.Source ?? ""));

        viewModel.OpenSettingsPageCommand.Execute("prompts.soul");
        Assert.True(viewModel.IsPromptSettings);
        Assert.Equal("prompts.soul", viewModel.SelectedPromptPreview?.Key);

        viewModel.OpenSettingsSectionCommand.Execute("Tools");
        Assert.True(viewModel.IsToolSettings);

        viewModel.OpenSettingsSectionCommand.Execute("Mcp");
        Assert.True(viewModel.IsMcpSettings);
        Assert.False(viewModel.IsPlannedSettings);

        viewModel.OpenSettingsPageCommand.Execute("local.profile");
        Assert.True(viewModel.IsProfileSettings);
        Assert.False(viewModel.IsAppearanceSettings);
        Assert.Equal(SettingsSection.Local, viewModel.SelectedSectionItem.Section);

        viewModel.OpenSettingsSectionCommand.Execute("Roleplay");
        Assert.Equal(SettingsSection.Roleplay, viewModel.SelectedSectionItem.Section);
        Assert.Equal("roleplay.behavior", viewModel.SelectedPageItem.Key);
        Assert.True(viewModel.IsPromptSettings);
        Assert.Equal("角色行为规范", viewModel.SelectedPageItem.Title);

        viewModel.OpenSettingsSectionCommand.Execute("Work");
        Assert.Equal("work.guide", viewModel.SelectedPageItem.Key);
        Assert.True(viewModel.IsPromptSettings);
        viewModel.OpenSettingsPageCommand.Execute("work.index");
        Assert.True(viewModel.IsWorkIndex);
        Assert.True(viewModel.SelectedPageItem.Title == "资料索引");
        viewModel.OpenSettingsPageCommand.Execute("work.subagents");
        Assert.True(viewModel.IsWorkSubagents);
        viewModel.OpenSettingsPageCommand.Execute("work.context");
        Assert.True(viewModel.IsWorkContext);

        viewModel.OpenSettingsPageCommand.Execute("roleplay.context");
        Assert.True(viewModel.IsRoleplayContext);
        Assert.False(viewModel.IsPlannedSettings);

        viewModel.OpenSettingsPageCommand.Execute("roleplay.heartbeat");
        Assert.True(viewModel.IsRoleplayHeartbeat);

        viewModel.OpenSettingsPageCommand.Execute("roleplay.states");
        Assert.True(viewModel.IsRoleplayStates);

        viewModel.OpenSettingsSectionCommand.Execute("Config");
        Assert.True(viewModel.IsConfigFileSettings);
        Assert.False(viewModel.IsPlannedSettings);
        Assert.Equal("config.file", viewModel.SelectedPageItem.Key);

        // 回到本机会恢复上次停留的子页
        viewModel.OpenSettingsSectionCommand.Execute("Local");
        Assert.True(viewModel.IsProfileSettings);

        // 八个一级菜单与子页键都不能重；占位页必须有实际配置分组（角色模式三页是自定义视图）
        Assert.Equal(8, viewModel.SettingsSections.Count);
        var pages = viewModel.SettingsSections.SelectMany(section => section.Pages).ToList();
        var keys = pages.Select(page => page.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        var customViews = new[] { "roleplay.context", "roleplay.heartbeat", "roleplay.states" };
        Assert.All(pages.Where(page => page.Status == SettingsPageStatus.Planned
                                       && !customViews.Contains(page.Key)), page =>
        {
            Assert.NotNull(page.Definition.Groups);
            Assert.NotEmpty(page.Definition.Groups!);
        });

        // 关闭请求交给窗口处理，不改主窗口的当前页
        viewModel.BackToChatCommand.Execute(null);
        Assert.Equal(1, closed);

        viewModel.OpenCronCommand.Execute(null);
        Assert.False(viewModel.IsSettingsSection);
        Assert.True(viewModel.IsCronSection);
        Assert.False(viewModel.IsPlaceholderSection);
    }

    [Fact]
    public void QuickToggleFlipsBetweenDarkAndLight()
    {
        var viewModel = new MainViewModel();

        viewModel.SetThemeCommand.Execute("Dark");
        Assert.Equal(IconGeometries.Sun, viewModel.ThemeToggleIcon);
        viewModel.ToggleThemeCommand.Execute(null);
        Assert.True(viewModel.IsLightTheme);
        Assert.Equal(IconGeometries.Moon, viewModel.ThemeToggleIcon);

        viewModel.ToggleThemeCommand.Execute(null);
        Assert.True(viewModel.IsDarkTheme);

        viewModel.SetThemeCommand.Execute("System");
        viewModel.ToggleThemeCommand.Execute(null);
        Assert.True(viewModel.IsDarkTheme || viewModel.IsLightTheme);
    }

    private static Avalonia.Media.Imaging.WriteableBitmap? Render(Window window, string name)
    {
        // 让过渡动画跑完（headless 下过渡按渲染 tick 推进，不按墙钟）
        for (var i = 0; i < 8; i++)
        {
            Thread.Sleep(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        var frame = window.CaptureRenderedFrame();

        var directory = Environment.GetEnvironmentVariable("SHOYEE_DESKTOP_SNAPSHOT");
        if (frame is not null && !string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
            frame.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        return frame;
    }
}
