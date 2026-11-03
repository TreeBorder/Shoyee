using System.Text.Json.Serialization;

namespace Shoyee.Desktop.Theming;

/// <summary>桌面设置（只属于本机窗口，不进 daemon 工作区配置）。</summary>
/// <param name="Theme">颜色模式：暗黑 / 明亮 / 跟随系统。</param>
/// <param name="Accent">强调色。</param>
/// <param name="ChatFillWidth">会话内容是否铺满宽度（false = 居中窄栏）。</param>
/// <param name="PortraitScale">立绘大小倍率（1.0 = 标准）。</param>
/// <param name="DisplayName">左栏显示的名字（默认「筱义」，不显示工作区路径）。</param>
/// <param name="Tagline">左栏名字下面的一行说明。</param>
/// <param name="WorkspacePath">工作区路径；接 daemon 前只作为本机配置保存，不参与连接。默认 <c>~/Shoyee</c> 只是占位。</param>
/// <param name="SidebarCollapsed">左侧菜单是否折叠成图标栏。</param>
/// <param name="UserAvatarPath">用户头像文件（绝对路径，空 = 默认剪影）。</param>
/// <param name="AgentAvatarPath">智能体头像文件（绝对路径，空 = 内置头像）。</param>
/// <param name="ImportPaths">会话导入来源 id → 上次使用的路径。</param>
/// <param name="ShowToolCalls">聊天区是否显示工具调用行。</param>
/// <param name="ShowReasoning">聊天区是否显示思考过程。</param>
/// <param name="SendOnEnter">发送快捷键：true = Enter 发送；false = Enter 换行。</param>
/// <param name="Models">模型配置：接入列表 + 场景绑定。</param>
/// <param name="MainModel">旧版主模型档位；只用于迁移早期 <c>desktop.json</c>，新保存不再写。</param>
/// <param name="SmallModel">旧版小模型档位；只用于迁移早期 <c>desktop.json</c>。</param>
/// <param name="VisionModel">旧版视觉模型档位；只用于迁移早期 <c>desktop.json</c>。</param>
/// <param name="WideBubbles">气泡是否平铺（false = 当前窄气泡）。</param>
/// <param name="PreviewCommand">预览命令模板；<c>{path}</c> 替换成文件绝对路径，空 = 平台默认（macOS Quick Look，其它系统默认程序）。</param>
/// <param name="SettingsVersion">设置文件版本，用来迁移旧默认值。</param>
public sealed record AppearanceSettings(
    ThemeMode Theme = ThemeMode.Dark,
    AccentColor Accent = AccentColor.Rose,
    bool ChatFillWidth = true,
    double PortraitScale = 1.0,
    string DisplayName = "筱义",
    string Tagline = "个人助理 · 情感陪伴",
    string WorkspacePath = "~/Shoyee",
    bool SidebarCollapsed = false,
    string? UserAvatarPath = null,
    string? AgentAvatarPath = null,
    Dictionary<string, string>? ImportPaths = null,
    bool ShowToolCalls = true,
    bool ShowReasoning = false,
    bool SendOnEnter = false,
    ModelCatalogSettings? Models = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ModelProfileSettings? MainModel = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ModelProfileSettings? SmallModel = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ModelProfileSettings? VisionModel = null,
    bool WideBubbles = false,
    string PreviewCommand = "",
    int SettingsVersion = 0);
