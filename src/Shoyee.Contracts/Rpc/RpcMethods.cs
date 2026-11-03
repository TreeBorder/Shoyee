namespace Shoyee.Contracts.Rpc;

/// <summary>JSON-RPC 方法名常量。禁止在实现里写字符串字面量。</summary>
public static class RpcMethods
{
    /// <summary>握手（连接后第一帧）。</summary>
    public const string Hello = "rpc.hello";

    /// <summary>保活（notification）。</summary>
    public const string Ping = "rpc.ping";

    /// <summary>会话列表。</summary>
    public const string SessionList = "session.list";

    /// <summary>会话详情。</summary>
    public const string SessionGet = "session.get";

    /// <summary>打开会话（不改 IM 指针）。</summary>
    public const string SessionOpen = "session.open";

    /// <summary>新建会话。</summary>
    public const string SessionCreate = "session.create";

    /// <summary>重命名会话。</summary>
    public const string SessionRename = "session.rename";

    /// <summary>归档会话。</summary>
    public const string SessionArchive = "session.archive";

    /// <summary>把会话设为某 Lane 的当前会话。</summary>
    public const string SessionSetActiveForLane = "session.setActiveForLane";

    /// <summary>分页取工作会话消息。</summary>
    public const string SessionMessages = "session.messages";

    /// <summary>分页取 RP Lane 历史（RP 没有 session，只读视图）。</summary>
    public const string LaneMessages = "lane.messages";

    /// <summary>跨会话检索。</summary>
    public const string SessionSearch = "session.search";

    /// <summary>取冻结提示词正文。</summary>
    public const string SessionSnapshot = "session.snapshot";

    /// <summary>导出会话。</summary>
    public const string SessionExport = "session.export";

    /// <summary>发送一轮。</summary>
    public const string TurnSend = "turn.send";

    /// <summary>取消当前轮。</summary>
    public const string TurnCancel = "turn.cancel";

    /// <summary>重跑上一轮。</summary>
    public const string TurnRetry = "turn.retry";

    /// <summary>答复审批。</summary>
    public const string ApprovalRespond = "approval.respond";

    /// <summary>合成语音。</summary>
    public const string TtsSynthesize = "tts.synthesize";

    /// <summary>生成角色图。</summary>
    public const string ImageRender = "image.render";

    /// <summary>进程状态。</summary>
    public const string StatusGet = "status.get";

    /// <summary>列出当前工具面（含启用状态与 MCP 来源）。</summary>
    public const string ToolsList = "tools.list";

    /// <summary>MCP 服务连接状态与每服务工具清单。</summary>
    public const string McpStatus = "mcp.status";

    /// <summary>重连 MCP 服务（新工具面要重启 daemon 才对运行中的 agent 生效）。</summary>
    public const string McpReload = "mcp.reload";

    /// <summary>改一个通用状态（心情/精力等），并推送 <c>state.updated</c>。</summary>
    public const string StateSet = "state.set";

    /// <summary>按会话或时间区间取用量。</summary>
    public const string UsageGet = "usage.get";

    /// <summary>读配置（凭据脱敏）。</summary>
    public const string ConfigGet = "config.get";

    /// <summary>写配置（局部合并或整份原文），随后按热更新规则生效。</summary>
    public const string ConfigSet = "config.set";

    /// <summary>重载配置（只影响新会话）。</summary>
    public const string ConfigReload = "config.reload";

    /// <summary>桌面读取工作区文件前先拿路径。</summary>
    public const string WorkspacePaths = "workspace.paths";

    /// <summary>列出 INDEX。</summary>
    public const string IndexList = "index.list";

    /// <summary>新增/更新 INDEX。</summary>
    public const string IndexUpsert = "index.upsert";

    /// <summary>删除 INDEX。</summary>
    public const string IndexRemove = "index.remove";

    /// <summary>取资产路径。</summary>
    public const string AssetGet = "asset.get";

    /// <summary>列定时任务。</summary>
    public const string CronList = "cron.list";

    /// <summary>定时任务详情（定义 + 最近执行）。</summary>
    public const string CronGet = "cron.get";

    /// <summary>分页取某任务的执行记录。</summary>
    public const string CronExecutions = "cron.executions";

    /// <summary>启停定时任务。</summary>
    public const string CronSetEnabled = "cron.setEnabled";

    /// <summary>删除定时任务（含执行台账）。</summary>
    public const string CronRemove = "cron.remove";

    /// <summary>发一条测试通知（配置完渠道后自检用）。</summary>
    public const string NotifyTest = "notify.test";

    /// <summary>daemon 轻量状态（ctl/menu）。</summary>
    public const string DaemonStatus = "daemon.status";

    /// <summary>优雅退出。</summary>
    public const string DaemonShutdown = "daemon.shutdown";

    /// <summary>重启 daemon（退出后由 launchd 拉起；无守护进程时等同于停机）。</summary>
    public const string DaemonRestart = "daemon.restart";
}
