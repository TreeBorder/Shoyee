using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Subagents;
using Shoyee.Contracts.Options;
using Shoyee.Sessions.Turns;
using Shoyee.Tools.Composition;

namespace Shoyee.Agent.Composition;

/// <summary>
/// 工作模式 agent 工厂：<see cref="HarnessAgent"/>（内置工具循环 + 审批链）。
///
/// 历史用框架 <see cref="InMemoryChatHistoryProvider"/>；建会话时把人设作为**第一条 system**
/// 写进 session 历史，此后只追加，随 session 序列化持久化。
/// 工具由 <see cref="ToolsetBuilder"/> 组装后从 <c>ChatOptions.Tools</c> 进（不再用 Harness 内置文件工具）。
/// </summary>
public sealed class WorkAgentFactory
{
    /// <summary>工作模式 agent 的稳定 id。</summary>
    public const string AgentId = "shoyee-work";

    private readonly IChatClient _main;
    private readonly InMemoryChatHistoryProvider _history;
    private readonly HarnessAgentOptions _options;

    /// <summary>创建工厂。</summary>
    /// <param name="main">主模型客户端。</param>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="history">历史 provider（测试可注入；默认新建）。</param>
    /// <param name="toolset">工具面（null = 不带工具）。</param>
    /// <param name="subagents">命名子智能体（空/ null = 不挂 <c>background_agents_*</c>）。</param>
    /// <param name="subagentOptions">子智能体等待策略（同步/异步、等待上限）。</param>
    /// <param name="compaction">上下文压缩（null = 默认关闭）。</param>
    /// <param name="compactionSummarizer">压缩用的摘要模型（通常是 small 档）；null = 不做压缩。</param>
    public WorkAgentFactory(
        IChatClient main,
        string workspaceRoot,
        InMemoryChatHistoryProvider? history = null,
        Toolset? toolset = null,
        IReadOnlyList<AIAgent>? subagents = null,
        SubagentOptions? subagentOptions = null,
        CompactionOptions? compaction = null,
        IChatClient? compactionSummarizer = null)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _main = main;
        _history = history ?? new InMemoryChatHistoryProvider();
        _options = new HarnessAgentOptions
        {
            Id = AgentId,
            Name = "Shoyee",
            ChatHistoryProvider = _history,
            ChatOptions = new ChatOptions { Tools = toolset?.Tools.ToList() },
            // 审批 HITL：包装过的写/执行工具进审批链，白名单与安全命令自动放行
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions(),
            MaximumIterationsPerRequest = 20,

            // 不背 Harness 全家桶：记忆=工作区文件、任务=子智能体、无技能库、web 走工具面。
            // 文件工具用 Shoyee.Tools 的实现（不由 Harness 内置，避免同名工具重复）。
            DisableFileMemory = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            DisableWebSearch = true,
            DisableOpenTelemetry = true,
        };
        _options.ToolApprovalAgentOptions!.AutoApprovalRules = toolset?.AutoApprovalRules ?? [];
        SubagentFactory.ApplyTo(_options, subagents ?? [], subagentOptions);

        // 入循环压缩：只压下一次请求实际发出去的消息，不改写已落库的历史。
        // 不设 MaxContextWindowTokens/MaxOutputTokens，避免顺带改掉 ChatOptions 的输出上限。
        _options.CompactionStrategy = ContextCompactionPolicy.Create(compaction, compactionSummarizer);
    }

    /// <summary>创建工作模式 agent。</summary>
    public HarnessAgent CreateAgent() => _main.AsHarnessAgent(_options);

    /// <summary>
    /// 建工作会话：新建框架 session，把人设写进历史第一条 system。
    /// **只在会话创建时调用一次**；resume 不调用（历史已在 <c>session_state</c> 里）。
    /// </summary>
    /// <param name="agent">工作模式 agent（必须是本工厂创建的，历史 provider 要一致）。</param>
    /// <param name="systemPrompt">已拼好的工作模式前置提示词。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AgentSession> CreateSessionAsync(
        AIAgent agent,
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        var session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        _history.SetMessages(session, [new ChatMessage(ChatRole.System, systemPrompt)]);
        return session;
    }

    /// <summary>修补历史里的悬空工具调用（中断/失败后、落盘前调用）。</summary>
    /// <param name="session">工作会话。</param>
    public void RepairHistory(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var repaired = ToolCallRepair.RepairDanglingToolCalls(_history.GetMessages(session));
        _history.SetMessages(session, repaired.ToList());
    }

    /// <summary>
    /// 开跑前清理（新回合唯一入口）：补悬空调用 + 清掉框架审批链的 StateBag 残留。
    ///
    /// 软打断/硬停可能落在「审批请求已浮出、结果未回」的窗口：历史里补了合成结果，
    /// 但框架 1.25.0 的 <c>_pendingApprovalRequests</c> 还留着；下一次请求
    /// <c>ApprovalResponseBindingChatClient</c> 会把它自动转成 rejected 结果，同一个 callId
    /// 叠出第二条 <c>role=tool</c>，OpenAI 兼容端点直接 400、会话永久毒化。
    /// 两个键是框架 internal 常量，跨程序集只能写死。
    /// </summary>
    /// <param name="session">工作会话。</param>
    public void PrepareTurn(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        RepairHistory(session);
        ClearApprovalResidue(session);
    }

    /// <summary>清框架审批链留在 session StateBag 的残留（见 <see cref="PrepareTurn"/>）。</summary>
    /// <param name="session">工作会话。</param>
    public void ClearApprovalResidue(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.StateBag.TryRemoveValue("_pendingApprovalRequests");
        session.StateBag.TryRemoveValue("_autoApprovedFunctionCalls");
    }

    /// <summary>读当前会话的完整历史（转录投影用）。</summary>
    /// <param name="session">工作会话。</param>
    public IReadOnlyList<ChatMessage> GetHistory(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _history.GetMessages(session).ToList();
    }

    /// <summary>整段替换历史（重试时把失败的输入退回去；其余场景不要用）。</summary>
    /// <param name="session">工作会话。</param>
    /// <param name="messages">新历史（必须仍以开场 system 起头）。</param>
    public void SetHistory(AgentSession session, IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(messages);
        _history.SetMessages(session, messages.ToList());
    }
}
