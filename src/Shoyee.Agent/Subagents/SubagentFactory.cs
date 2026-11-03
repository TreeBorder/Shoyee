using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;

namespace Shoyee.Agent.Subagents;

/// <summary>
/// 命名子智能体装配（框架 <c>BackgroundAgents</c>）：每个子 agent = 主模型 + 聚焦指令 + 最小工具。
/// </summary>
/// <remarks>
/// <para>形状照 Secretary 已验证的做法：有哪些子智能体、各自什么指令全在 <c>config.json</c> 里写，不预置死；
/// 没配 = 工作 agent 不挂 <c>background_agents_*</c>。</para>
/// <para>审批不透传父 agent，所以这里的工具**一律免审批**：只给只读能力 + 工作区文件读写 + MCP，
/// <c>terminal</c> 只有 <see cref="SubagentDefinition.AllowShell"/> 显式开。子 agent 不带
/// <c>BackgroundAgents</c>，深度天然限 1。</para>
/// </remarks>
public static class SubagentFactory
{
    /// <summary>
    /// 异步模式（<c>autoWait=false</c>）替换框架默认指令：默认文案里有一句「Always wait for outstanding tasks」，
    /// 不覆盖的话模型会自己把回合堵在等待上（Secretary 实测）。<c>{background_agents}</c> 占位符必须保留，
    /// 框架用它替换成在岗子智能体名单。
    /// </summary>
    public const string AsyncInstructions =
        """
        ## BackgroundAgents
        You have access to background agents that can perform work on your behalf.

        - Use the `background_agents_*` tools to start tasks on background agents and check their results.
        - Starting a task does not block; background tasks run concurrently.
        - Tasks run asynchronously: reply to the user right after dispatching; do not block the conversation waiting for them. Only call `background_agents_wait_for_first_completion` when the user explicitly asks you to wait for the outcome now.
        - Check or retrieve results with `background_agents_get_all_tasks` / `background_agents_get_task_results` when the user asks about a task, or before building further work on its output.
        - After retrieving results from a completed task, clear it with `background_agents_clear_completed_task` to free memory, unless you plan to continue it with `background_agents_continue_task`.

        {background_agents}
        """;

    /// <summary>默认给子智能体的只读工具名（白名单写了就按名字限缩这一组）。</summary>
    private static readonly HashSet<string> DefaultToolNames = new(StringComparer.Ordinal)
    {
        "session_search",
        "session_read",
        "index_list",
        "notes_search",
        "notes_read",
        "web_search",
        "web_fetch",
        "analyze_image",
        "voice_transcribe",
    };

    /// <summary>文件工具前缀：<c>file_access_*</c> 始终可用（免审批版），不受白名单限缩。</summary>
    private const string FileToolPrefix = "file_access_";

    /// <summary>终端工具名（只随 <see cref="SubagentDefinition.AllowShell"/> 出现）。</summary>
    private const string ShellToolName = "terminal";

    /// <summary>
    /// 按配置建子智能体。空名 / 重名（大小写不敏感）的定义跳过，不拦启动。
    /// </summary>
    /// <param name="options">子智能体配置（null 或空列表 = 不启用）。</param>
    /// <param name="candidates">免责白名单的来源：工作模式**免审批**工具集（<c>Build(ToolProfile.Work, requireApproval: false)</c>）。</param>
    /// <param name="mcpToolNames">MCP 工具名（归默认组，白名单可以限缩）。</param>
    /// <param name="chatClientFactory">主模型客户端工厂（测试可注入）。</param>
    /// <param name="notificationPusher">终态通知实现（仅 <see cref="SubagentOptions.NotifyOnComplete"/> 时用；null = 不推）。</param>
    /// <param name="logger">日志（可空）。</param>
    public static IReadOnlyList<AIAgent> Build(
        SubagentOptions? options,
        IReadOnlyList<AITool> candidates,
        IReadOnlyCollection<string>? mcpToolNames = null,
        Func<IChatClient>? chatClientFactory = null,
        INotificationPusher? notificationPusher = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (options is null || options.EffectiveAgents.Count == 0)
        {
            return [];
        }

        var client = chatClientFactory?.Invoke()
                     ?? throw new ArgumentNullException(nameof(chatClientFactory), "启用子智能体时必须提供主模型客户端工厂");
        var mcp = new HashSet<string>(mcpToolNames ?? [], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var agents = new List<AIAgent>();
        foreach (var definition in options.EffectiveAgents)
        {
            var name = definition.Name?.Trim() ?? string.Empty;
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            AIAgent agent = client.AsAIAgent(new ChatClientAgentOptions
            {
                Id = "sub-" + name.ToLowerInvariant(),
                Name = name,
                Description = string.IsNullOrWhiteSpace(definition.Description) ? name : definition.Description,
                ChatOptions = new ChatOptions
                {
                    Instructions = definition.Instructions,
                    Tools = SelectTools(definition, candidates, mcp),
                },
            });

            if (options.NotifyOnComplete && notificationPusher is not null)
            {
                agent = new NotifyingAgent(agent, notificationPusher, logger);
            }

            agents.Add(agent);
        }

        return agents;
    }

    /// <summary>把子智能体与等待策略挂到工作 agent 的选项上（无子智能体时什么都不做）。</summary>
    /// <param name="target">工作 agent 选项。</param>
    /// <param name="agents">子智能体列表。</param>
    /// <param name="options">子智能体配置（决定同步/异步与等待上限）。</param>
    public static void ApplyTo(HarnessAgentOptions target, IReadOnlyList<AIAgent> agents, SubagentOptions? options)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(agents);
        if (agents.Count == 0)
        {
            return;
        }

        target.BackgroundAgents = agents;
        var providerOptions = new BackgroundAgentsProviderOptions
        {
            WaitTimeout = TimeSpan.FromSeconds(Math.Max(1, options?.WaitTimeoutSeconds ?? 300)),
        };

        if (options?.AutoWait == true)
        {
            // 同步模式：框架的自动等待循环，回合内把任务收完（waitTimeout/maxLoopIterations 是回合级闸）
            target.LoopEvaluators = [new BackgroundTaskCompletionLoopEvaluator()];
            target.LoopAgentOptions = new LoopAgentOptions
            {
                MaxIterations = Math.Max(1, options.MaxLoopIterations),
            };
        }
        else
        {
            // 异步模式：不挂 evaluator，并把框架默认的「必须等完」指令换掉
            providerOptions.Instructions = AsyncInstructions;
        }

        target.BackgroundAgentsProviderOptions = providerOptions;
    }

    /// <summary>
    /// 选工具：文件工具始终给；<c>terminal</c> 按 <c>AllowShell</c>；其余从「默认组（只读 + MCP）」里选，
    /// 写了白名单就按名字限缩。
    /// </summary>
    private static List<AITool> SelectTools(
        SubagentDefinition definition,
        IReadOnlyList<AITool> candidates,
        HashSet<string> mcpToolNames)
    {
        var whitelist = definition.Tools.Count == 0 ? null : new HashSet<string>(definition.Tools, StringComparer.Ordinal);
        var tools = new List<AITool>();
        var fileTools = new List<AITool>();
        AITool? shell = null;

        foreach (var tool in candidates)
        {
            if (tool is not AIFunction function || string.IsNullOrWhiteSpace(function.Name))
            {
                continue;
            }

            var name = function.Name;
            if (name.StartsWith(FileToolPrefix, StringComparison.Ordinal))
            {
                fileTools.Add(tool);
                continue;
            }

            if (name == ShellToolName)
            {
                shell = tool;
                continue;
            }

            var isDefault = DefaultToolNames.Contains(name) || mcpToolNames.Contains(name);
            if (whitelist is null ? isDefault : whitelist.Contains(name))
            {
                tools.Add(tool);
            }
        }

        if (definition.AllowShell && shell is not null)
        {
            tools.Add(shell);
        }

        tools.AddRange(fileTools);
        return tools;
    }
}
