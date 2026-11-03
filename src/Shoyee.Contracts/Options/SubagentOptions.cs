namespace Shoyee.Contracts.Options;

/// <summary>
/// 子智能体配置（<c>config.json</c> 的 <c>tools.subagents</c> 段）。没配或空列表 = 不启用，
/// 工作 agent 不挂 <c>background_agents_*</c> 工具族。
/// </summary>
/// <remarks>
/// 只给工作模式：RP 走普通 <c>ChatClientAgent</c>，没有审批中间件，也不该有执行类子任务。
/// 只有可信的子智能体才该注册——它们的产出会回到父 agent 上下文。
/// </remarks>
/// <param name="Agents">命名子智能体（人设即产品：有哪些、各干什么全在配置里写，不预置死）。</param>
/// <param name="AutoWait">
/// 派活后她的回合是否被**自动等待**拖住（框架 <c>BackgroundTaskCompletionLoopEvaluator</c>）。
/// false（默认）= 异步：派完即回，结果她用 <c>background_agents_get_*</c> 自查；
/// true = 回合内等任务跑完，短任务结果直接带回。
/// </param>
/// <param name="WaitTimeoutSeconds"><c>background_agents_wait_for_first_completion</c> 单次等待上限；超时任务照跑、可以再等。</param>
/// <param name="MaxLoopIterations">自动等待循环（仅 <paramref name="AutoWait"/> = true 生效）的最大迭代数。</param>
/// <param name="NotifyOnComplete">
/// 子任务终态是否推一条纯通知（Server酱，不走聊天渠道）。默认关：推送会打扰手机，
/// 且异步任务的正文她本来就能用 <c>background_agents_get_*</c> 取回。开关只决定「要不要主动告知」。
/// </param>
public sealed record SubagentOptions(
    IReadOnlyList<SubagentDefinition>? Agents = null,
    bool AutoWait = false,
    int WaitTimeoutSeconds = 300,
    int MaxLoopIterations = 10,
    bool NotifyOnComplete = false)
{
    /// <summary>实际启用的子智能体（没配就是空列表）。</summary>
    public IReadOnlyList<SubagentDefinition> EffectiveAgents => Agents ?? [];
}

/// <summary>
/// 一个命名子智能体（框架 <c>BackgroundAgents</c>）：子 agent = 主模型 + 聚焦指令 + 最小工具。
/// </summary>
/// <remarks>
/// 子任务的审批**不会**透传回父 agent（框架语义），所以子 agent 的工具一律免审批——
/// 危险能力（<c>terminal</c>）只有显式 <see cref="AllowShell"/> 才给。
/// 子 agent 不带 <c>BackgroundAgents</c>，深度天然限 1。
/// </remarks>
public sealed class SubagentDefinition
{
    /// <summary>名字（必填；框架要求非空且大小写不敏感唯一，重名/空名的定义会被跳过）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>她是干什么的（父 agent 按这段描述选人派活）。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>聚焦指令（人设/规程；框架建议只给这个角色真正需要的工具）。</summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>
    /// 工具名白名单（空 = 默认集：只读工具 + MCP）。写了就按名字**限缩**默认集；
    /// <c>file_access_*</c> 始终可用（免审批版），不必列；<c>terminal</c> 用 <see cref="AllowShell"/> 开。
    /// </summary>
    public List<string> Tools { get; set; } = new();

    /// <summary>
    /// 是否给 <c>terminal</c>。给了就是**免审批执行**（子任务审批不透传父 agent），危险，默认关闭。
    /// </summary>
    public bool AllowShell { get; set; }
}
