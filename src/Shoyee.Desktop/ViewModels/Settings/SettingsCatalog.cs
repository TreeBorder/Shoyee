using Shoyee.Desktop.Theming;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页的信息架构：一级菜单 → 二级子页 → 每页的配置分组。
///
/// 目录照 <c>docs/设置页信息架构-v2.md</c>；这里只描述「有哪些页、每页有哪些字段、从哪取数」，
/// 不做 RPC 也不读文件。值是实际运行配置的样例，接后端时把 <see cref="SettingsFieldItem.Value"/>
/// 换成绑定即可，导航和表单布局都不用动。
/// </summary>
public static class SettingsCatalog
{
    /// <summary>全部一级菜单（按显示顺序）。</summary>
    public static IReadOnlyList<SettingsSectionItem> Sections { get; } =
    [
        new(
            SettingsSection.Local,
            "本机",
            "只影响这台机器：外观、资料与头像、工作区与连接。",
            IconGeometries.Monitor,
            [
                new(
                    "local.appearance",
                    "外观与对话",
                    "颜色模式、主题色、会话宽度与角色立绘大小。",
                    "desktop.json（本机偏好，不进 daemon 工作区）",
                    SettingsPageStatus.Live,
                    []),
                new(
                    "local.profile",
                    "资料与头像",
                    "左栏显示名、第二行说明与两个头像。",
                    "desktop.json + 应用数据目录的 avatars/",
                    SettingsPageStatus.Live,
                    []),
                new(
                    "local.connection",
                    "工作区与连接",
                    "工作区路径、daemon 连接状态与桌面文件位置。",
                    "desktop.json + status.get",
                    SettingsPageStatus.Live,
                    ["daemon 状态现为占位，接 RpcClient 后展示 hello / ping / 在线 Lane"]),
            ]),

        new(
            SettingsSection.Prompts,
            "提示词",
            "角色与工作共用的四篇文件；只读预览，编辑用系统默认编辑器。",
            IconGeometries.Document,
            [
                new(
                    "prompts.agents",
                    "通用规则",
                    "两个模式共用的行为约定：工作方式、边界与工具使用原则。",
                    "{workspace}/AGENTS.shared.md",
                    SettingsPageStatus.Live,
                    ["接 workspace.paths 后直读后换成真实文本；现在的内容是结构占位"]),
                new(
                    "prompts.soul",
                    "角色人设",
                    "她是谁、怎么说话；会话创建时读一次并冻结进快照。",
                    "{workspace}/SOUL.md",
                    SettingsPageStatus.Live,
                    ["接 workspace.paths 后直读；改完要 /new 或新建会话才生效"]),
                new(
                    "prompts.user",
                    "用户人设",
                    "用户的称呼、偏好与长期需要记住的个人信息。",
                    "{workspace}/USER.md",
                    SettingsPageStatus.Live,
                    ["接 workspace.paths 后直读；写记忆仍走文件工具"]),
                new(
                    "prompts.memory",
                    "长期记忆",
                    "关系变化、承诺与还没做完的事。",
                    "{workspace}/MEMORY.md",
                    SettingsPageStatus.Live,
                    ["接 workspace.paths 后直读；不做每日自动归并"]),
            ]),

        new(
            SettingsSection.Roleplay,
            "角色模式",
            "角色专属的守则、摘要、心跳与状态；工作模式的配置不放在这里。",
            IconGeometries.Chat,
            [
                new(
                    "roleplay.behavior",
                    "角色行为规范",
                    "她怎么说话、哪些动作不做、回复多长；每轮组装时注入。",
                    "modes/roleplay/AGENTS.md",
                    SettingsPageStatus.Live,
                    ["workspace.paths 返回路径", "只读预览 + 系统编辑器打开"]),
                new(
                    "roleplay.world",
                    "世界观设定",
                    "关系、环境与日常背景；和角色行为规范分开维护。",
                    "modes/roleplay/WORLD.md",
                    SettingsPageStatus.Live,
                    ["workspace.paths 返回路径", "只读预览 + 系统编辑器打开"]),
                new(
                    "roleplay.context",
                    "摘要与上下文",
                    "三个数字配置 + 一段摘要提示词。",
                    "运行中的 config：roleplay.summaryEveryRounds / contextRounds / redundantMessages",
                    SettingsPageStatus.Planned,
                    ["三个配置项进 config.json", "摘要 system prompt 的来源"]),
                new(
                    "roleplay.heartbeat",
                    "心跳与惊喜问候",
                    "心跳参数与四组问候提示词；提示词可直接改。",
                    "proactive.heartbeat + {workspace}/.heartbeat/prompts.json、heartbeat.log",
                    SettingsPageStatus.Planned,
                    ["prompts.json 读写", "heartbeat.log 尾部 N 行"]),
                new(
                    "roleplay.states",
                    "状态与场景",
                    "状态表的当前值、默认值与写入口。",
                    "状态表（scene / outfit 等）",
                    SettingsPageStatus.Planned,
                    ["状态读取 RPC 或直接读库", "默认值与当前值的区分"]),
            ]),

        new(
            SettingsSection.Work,
            "工作模式",
            "工作专属的提示词、资料索引、子智能体与会话上下文规则。",
            IconGeometries.Briefcase,
            [
                new(
                    "work.guide",
                    "工作指南",
                    "工作模式的首条 system 里注入的守则：干活流、会话习惯与可靠性。",
                    "modes/work/AGENTS.md",
                    SettingsPageStatus.Live,
                    ["workspace.paths 返回路径", "只读预览 + 系统编辑器打开"]),
                new(
                    "work.index",
                    "资料索引",
                    "会话开局只注入「名称｜注释｜地址」；可启用 / 停用 / 删除。",
                    "index.list / index.upsert / index.remove + index_entries.enabled",
                    SettingsPageStatus.Live,
                    ["表加 enabled 列", "前端搜索、筛选与分页"]),
                new(
                    "work.subagents",
                    "子智能体",
                    "启用哪些子智能体、各自工具白名单、是否同步等待。",
                    "tools.subagents.agents[]",
                    SettingsPageStatus.Live,
                    ["配置编辑与校验", "保存后提示「重启 daemon 生效」"]),
                new(
                    "work.context",
                    "会话与上下文",
                    "上下文压缩默认关闭；打开后 500K 以下不压，触发水位取模型窗口-输出，压到 500K、保留 32 组、间隔 30 分钟。",
                    "context.compaction（config）",
                    SettingsPageStatus.Live,
                    ["接 config.get / config.set 后绑定", "改完重启 daemon 生效"]),
            ]),

        new(
            SettingsSection.Tools,
            "工具",
            "工作 / 角色两种工具面、禁用名单与审批规则。",
            IconGeometries.Wrench,
            [
                new(
                    "tools.set",
                    "工具集",
                    "按组列出当前挂载的工具与启用状态。",
                    "ToolsetBuilder 的 Work / Roleplay profile + tools.disabled",
                    SettingsPageStatus.Live,
                    ["接运行配置后支持搜索、分组筛选与分页"]),
                new(
                    "tools.approval",
                    "审批规则",
                    "只读免审批、危险命令与路径黑名单。",
                    "tools.autoApproveReadOnly、shell.denyPatterns、tools.deniedPaths",
                    SettingsPageStatus.Planned,
                    ["config.set + 校验", "保存前提示生效范围（立即 / 新对话 / 重启）"],
                    [
                        Group("审批", "工作模式才有审批中间件；RP 没有。",
                            Field("只读免审批", "true", SettingsFieldKind.Switch),
                            Field("工作区 / 笔记库内写免审批", "true", SettingsFieldKind.Switch),
                            Field("MCP 是否包装审批", "否（与 Secretary 一致）", SettingsFieldKind.ReadOnly)),
                        Group("命令黑名单", "命中即拒绝，不进入审批。",
                            Field("denyPatterns", """
                                rm\s+-rf\s+/
                                mkfs
                                dd\s+if=
                                :\(\)\s*\{
                                """, SettingsFieldKind.Multiline)),
                        Group("路径黑名单", "读写两侧都拦；`ls` 过滤条目、`grep` 跳过文件。",
                            Field("deniedPaths", """
                                {workspace}/assets/images
                                ~/Library/Keychains
                                """, SettingsFieldKind.Multiline)),
                    ]),
            ]),

        new(
            SettingsSection.Mcp,
            "MCP",
            "MCP 服务器配置、连接状态与每个服务的工具白名单。",
            IconGeometries.Extension,
            [
                new(
                    "mcp.servers",
                    "服务器",
                    "已配置的 MCP 服务与连接状态。",
                    "tools.mcp[] 运行配置 + 连接状态",
                    SettingsPageStatus.Live,
                    ["接真实连接状态后显示 lastError", "重连走新增 mcp.reload"]),
                new(
                    "mcp.tools",
                    "工具与白名单",
                    "每个服务实际暴露的工具，取消勾选即禁用。",
                    "需新增状态接口（当前没有工具清单）",
                    SettingsPageStatus.Planned,
                    ["daemon 侧暴露每个 server 的工具清单", "禁用名单写回 tools.mcp[]"],
                    [
                        Group("obsidian", "stdio · 已连接。",
                            Field("notes_search", "已启用", SettingsFieldKind.ReadOnly),
                            Field("notes_read", "已启用", SettingsFieldKind.ReadOnly),
                            Field("notes_write", "未提供（本地库不实现）", SettingsFieldKind.ReadOnly)),
                        Group("playwright", "http://127.0.0.1:3001 · 未连接。",
                            Field("工具清单", "未连接时取不到；连上后按服务端返回列出", SettingsFieldKind.ReadOnly),
                            Field("重连", "mcp.reload（待新增）", SettingsFieldKind.ReadOnly)),
                    ]),
            ]),

        new(
            SettingsSection.Providers,
            "供应商与配置",
            "模型、渠道、通知与各项能力供应商；改完按生效范围提示。",
            IconGeometries.Cloud,
            [
                new(
                    "providers.models",
                    "模型配置",
                    "上面选三个场景各用哪个模型，下面管理接入的模型列表。",
                    "desktop.json（本机展示值）；接 daemon 后按场景换算成 models.main / small / vision",
                    SettingsPageStatus.Live,
                    ["config.set + 字段校验", "保存前做一次连通性检查"]),
                new(
                    "providers.channels",
                    "渠道账号",
                    "Mattermost / 微信账号；添加时先选渠道类型。",
                    "channels.*",
                    SettingsPageStatus.Live,
                    ["config.set + 校验", "连接状态来自适配器"]),
                new(
                    "providers.notify",
                    "通知",
                    "通知供应商与自检；添加时先选供应商类型。",
                    "notify.* + notify.test",
                    SettingsPageStatus.Live,
                    ["配置表单", "notify.test 按钮"]),
                new(
                    "providers.voice",
                    "语音",
                    "语音合成与转写供应商。",
                    "voice.*",
                    SettingsPageStatus.Planned,
                    ["分供应商表单", "保存前校验端点可达"],
                    [
                        Group("语音", "Mimo 两向都有；Apple 只有转写。",
                            Field("provider", "mimo", SettingsFieldKind.Text),
                            Field("合成", "mimo", SettingsFieldKind.Text),
                            Field("转写", "apple", SettingsFieldKind.Text),
                            Field("Mimo base_url", "https://api.mimo.ai/v1", SettingsFieldKind.Text),
                            Field("Mimo key 环境变量", "SHOYEE_MIMO_API_KEY", SettingsFieldKind.Text)),
                    ]),
                new(
                    "providers.image",
                    "图片",
                    "ComfyUI 出图端点、模板与角色。",
                    "photo.*",
                    SettingsPageStatus.Planned,
                    ["分供应商表单", "保存前校验端点可达"],
                    [
                        Group("图片", "ComfyUI 模板替换 + 异步出图。",
                            Field("provider", "comfy", SettingsFieldKind.Text),
                            Field("ComfyUI 端点", "http://127.0.0.1:8333", SettingsFieldKind.Text),
                            Field("角色 key", "xiaoyi", SettingsFieldKind.Text),
                            Field("出图目录", "{workspace}/assets/images", SettingsFieldKind.ReadOnly)),
                    ]),
                new(
                    "providers.web",
                    "网页",
                    "检索与抓取供应商。",
                    "web.*",
                    SettingsPageStatus.Planned,
                    ["分供应商表单", "保存前校验端点可达"],
                    [
                        Group("网页", "DeepSeek 检索与抓取。",
                            Field("provider", "deepseek", SettingsFieldKind.Text),
                            Field("base_url", "https://api.deepseek.com/v1", SettingsFieldKind.Text),
                            Field("key 环境变量", "SHOYEE_WEB_API_KEY", SettingsFieldKind.Text)),
                    ]),
                new(
                    "providers.notes",
                    "笔记库",
                    "Obsidian vault 与笔记目录。",
                    "notes.*",
                    SettingsPageStatus.Planned,
                    ["分供应商表单", "保存前校验路径存在"],
                    [
                        Group("笔记库", "Obsidian vault；本地检索走 rg。",
                            Field("provider", "obsidian", SettingsFieldKind.Text),
                            Field("vaultRoot", "~/Akasha", SettingsFieldKind.Text),
                            Field("notesDir", "20-Obsidian", SettingsFieldKind.Text)),
                    ]),
                new(
                    "providers.runtime",
                    "Shell / 附件 / 数据库",
                    "终端、附件自动分析、数据库路径。",
                    "shell.* / attachments.* / databasePath",
                    SettingsPageStatus.Planned,
                    ["config.set + 校验", "标出需要重启的项"],
                    [
                        Group("Shell", "工作目录与超时；黑名单见「工具 · 审批规则」。",
                            Field("工作目录", "{workspace}", SettingsFieldKind.Text),
                            Field("超时（秒）", "120", SettingsFieldKind.Number),
                            Field("执行器", "LocalShellExecutor · Stateless", SettingsFieldKind.ReadOnly)),
                        Group("附件", "图片走视觉端点，语音走转写；失败静默降级。",
                            Field("自动分析", "true", SettingsFieldKind.Switch),
                            Field("图片提示词", "描述这张图里与对话有关的内容，不要展开无关细节。", SettingsFieldKind.Multiline),
                            Field("超时（秒）", "30", SettingsFieldKind.Number)),
                        Group("数据库", "SQLite + FTS5。",
                            Field("databasePath", "{workspace}/shoyee.db", SettingsFieldKind.Text),
                            Field("databaseProvider", "sqlite", SettingsFieldKind.ReadOnly),
                            Field("迁移", "启动时自动迁移", SettingsFieldKind.ReadOnly)),
                    ]),
            ]),

        new(
            SettingsSection.Config,
            "配置文件",
            "直接看配置原文、心跳实际配置与日志；重载与重启也在这里。",
            IconGeometries.Code,
            [
                new(
                    "config.file",
                    "config.json",
                    "结构化配置 + 原文编辑。",
                    "config.get / config.set",
                    SettingsPageStatus.Planned,
                    ["config.set 与校验", "原文与结构化视图互相同步"],
                    [
                        Group("概览", "daemon 当前生效的配置。",
                            Field("路径", "{workspace}/config.json", SettingsFieldKind.ReadOnly),
                            Field("加载时间", "2026-10-29 17:24", SettingsFieldKind.ReadOnly),
                            Field("热更新范围", "tools.deniedPaths / shell.denyPatterns / attachments", SettingsFieldKind.ReadOnly)),
                        Group("原文", "接 config.get 后展示完整 JSON；编辑走 config.set。",
                            Field("JSON", """
                                {
                                  "models": { "main": { "baseUrl": "https://api.deepseek.com/v1" } },
                                  "channels": { "mattermost": { "enabled": true } },
                                  "notify": { "provider": "system" },
                                  "proactive": { "heartbeat": { "enabled": true } },
                                  "tools": { "deniedPaths": [] }
                                }
                                """, SettingsFieldKind.Multiline)),
                    ]),
                new(
                    "config.heartbeat",
                    "心跳实际配置",
                    "daemon 当前生效的心跳参数与提示词，只读。",
                    "运行中的 proactive.heartbeat + prompts.json",
                    SettingsPageStatus.Planned,
                    ["与角色模式的心跳页共用同一份取数"],
                    [
                        Group("生效值", "不是代码默认值，是 daemon 现在用的值。",
                            Field("Enabled", "true", SettingsFieldKind.Switch),
                            Field("Min / Max 分钟", "45 / 75", SettingsFieldKind.Text),
                            Field("HotZone / Cooldown", "20 / 40", SettingsFieldKind.Text),
                            Field("TriggerPenalty", "50", SettingsFieldKind.Number),
                            Field("LongingCap", "100", SettingsFieldKind.Number),
                            Field("LevelThresholds", "15 / 30 / 50 / 70", SettingsFieldKind.Text),
                            Field("静默", "23:00 – 06:30", SettingsFieldKind.Text)),
                        Group("文件", "同目录下有提示词与运行日志。",
                            Field("prompts.json", "Text / Story / Voice / Image · 各 l1–l5", SettingsFieldKind.ReadOnly),
                            Field("heartbeat.log", "最近 100 条", SettingsFieldKind.ReadOnly)),
                    ]),
                new(
                    "config.logs",
                    "日志",
                    "daemon 日志路径与尾部内容。",
                    "logs/daemon.log",
                    SettingsPageStatus.Planned,
                    ["日志读取接口或本地直读", "刷新与打开所在目录"],
                    [
                        Group("日志文件", "JSON 行 + 文本混排，按天滚动。",
                            Field("路径", "~/Library/Application Support/Shoyee/logs/daemon.log", SettingsFieldKind.ReadOnly),
                            Field("级别", "information", SettingsFieldKind.Text),
                            Field("保留天数", "7", SettingsFieldKind.Number)),
                        Group("尾部", "接日志读取接口后换成最后 N 行。",
                            Field("最近输出", """
                                17:24:01 daemon 启动
                                17:24:01 迁移完成 · FTS 就绪
                                17:24:01 lanes 播种完成
                                17:24:01 监听 ~/Library/Application Support/Shoyee/daemon.sock
                                """, SettingsFieldKind.Multiline)),
                    ]),
                new(
                    "config.maintenance",
                    "重载 / 重启",
                    "热重载配置、重启 daemon、查看重启项。",
                    "config.reload / daemon.shutdown / ctl restart",
                    SettingsPageStatus.Planned,
                    ["重载结果（changed / restartRequired）展示", "重启前确认"],
                    [
                        Group("重载", "只热更新不碰模型请求前缀的项。",
                            Field("上次结果", "changed: false · restartRequired: []", SettingsFieldKind.ReadOnly),
                            Field("可热更新", "tools.deniedPaths / shell.denyPatterns / attachments", SettingsFieldKind.ReadOnly)),
                        Group("重启", "工具、MCP、模型变化需要重启。",
                            Field("停机方式", "daemon.shutdown RPC", SettingsFieldKind.ReadOnly),
                            Field("等待轮次", "10 秒", SettingsFieldKind.Number),
                            Field("需要重启的项", "models / tools / channels / proactive / voice / photo / web / notes / notify", SettingsFieldKind.ReadOnly)),
                    ]),
            ]),
    ];

    /// <summary>按一级菜单取子页。</summary>
    /// <param name="section">一级菜单。</param>
    public static SettingsSectionItem Get(SettingsSection section) =>
        Sections.First(item => item.Section == section);

    /// <summary>按一级菜单取它的第一页。</summary>
    /// <param name="section">一级菜单。</param>
    public static SettingsPageItem FirstPage(SettingsSection section) => Get(section).Pages[0];

    /// <summary>按子页键查页面；找不到返回 null。</summary>
    /// <param name="key">子页键（如 <c>local.appearance</c>）。</param>
    public static SettingsPageItem? FindPage(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : Sections.SelectMany(section => section.Pages)
                .FirstOrDefault(page => string.Equals(page.Key, key, StringComparison.OrdinalIgnoreCase));

    private static SettingsFieldItem Field(
        string label,
        string value,
        SettingsFieldKind kind = SettingsFieldKind.Text,
        string? hint = null) => new(label, value, kind, hint);

    private static SettingsGroupItem Group(
        string title,
        string description,
        params SettingsFieldItem[] fields) => new(title, description, fields);
}
