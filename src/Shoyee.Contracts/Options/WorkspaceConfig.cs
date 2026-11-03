namespace Shoyee.Contracts.Options;

/// <summary>
/// 工作区配置（<c>{workspace}/config.json</c>）。人设/提示词不在这里，它们是 Markdown 文件。
/// 凭据一律写环境变量名，配置文件里不出现明文。
/// </summary>
/// <param name="Root">工作区根目录。</param>
/// <param name="Models">模型档位（主/小/视觉）。</param>
/// <param name="Channels">渠道账号。</param>
/// <param name="Proactive">主动行为配置。</param>
/// <param name="Tools">工具与 MCP 配置。</param>
/// <param name="Photo">图片能力（可空 = 不启用角色图与生图工具）。</param>
/// <param name="Voice">语音合成/转写（可空 = 不启用语音工具）。</param>
/// <param name="DatabasePath">库文件路径或连接串（SQLite 为文件路径，默认 <c>{Root}/state.db</c>）。</param>
/// <param name="DatabaseProvider">存储提供方：<c>sqlite</c>（默认）/ <c>postgres</c>（预留）。</param>
/// <param name="Notes">笔记库（可空 = 不启用笔记检索；读/写笔记仍走文件工具）。</param>
/// <param name="Shell">受限终端（可空 = 默认工作区内的 Workspace/、60s 超时）。</param>
/// <param name="Web">网页搜索/抓取（可空 = 用 DeepSeek 默认端点）。</param>
/// <param name="Attachments">入站附件自动分析（可空 = 默认开启）。</param>
/// <param name="Notify">纯通知推送（可空 = 默认系统通知）。</param>
/// <param name="Context">会话上下文压缩（可空 = 默认开启、高阈值兜底）。</param>
public sealed record WorkspaceConfig(
    string Root,
    ModelProfiles Models,
    ChannelAccounts Channels,
    ProactiveOptions Proactive,
    ToolOptions Tools,
    PhotoOptions? Photo = null,
    VoiceOptions? Voice = null,
    string? DatabasePath = null,
    string DatabaseProvider = "sqlite",
    NotesOptions? Notes = null,
    ShellOptions? Shell = null,
    WebOptions? Web = null,
    AttachmentOptions? Attachments = null,
    NotifyOptions? Notify = null,
    ContextOptions? Context = null)
{
    /// <summary>实际库文件路径。</summary>
    public string ResolveDatabasePath()
        => string.IsNullOrWhiteSpace(DatabasePath) ? Path.Combine(Root, "state.db") : DatabasePath;
}
