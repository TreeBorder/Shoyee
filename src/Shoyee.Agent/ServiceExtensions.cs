using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shoyee.Agent.Approval;
using Shoyee.Agent.Composition;
using Shoyee.Agent.Media;
using Shoyee.Agent.Models;
using Shoyee.Agent.Process;
using Shoyee.Agent.Runtime;
using Shoyee.Agent.Runtime.Roleplay;
using Shoyee.Agent.Runtime.Work;
using Shoyee.Agent.Sessions;
using Shoyee.Agent.Subagents;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;
using Shoyee.Sessions.Persistence;
using Shoyee.Sessions.Roleplay;
using Shoyee.Sessions.Summary;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Mcp;
using Shoyee.Tools.Process;

namespace Shoyee.Agent;

/// <summary>代理层注册入口（Daemon 组合根调用）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册模型客户端、双模式 agent、轮次运行时与审批台。</summary>
    /// <param name="services">服务集合。</param>
    /// <remarks>
    /// <see cref="RoundRecorder"/> 不单独注册：它带每轮缓冲，必须与 RP agent 同生共死，
    /// 所以在 <see cref="RoleplayTurnRunner"/> 的工厂里成对创建。
    /// </remarks>
    public static IServiceCollection AddShoyeeAgent(this IServiceCollection services)
    {
        services.TryAddSingleton<ModelClientFactory>();

        services.TryAddSingleton(static sp =>
        {
            var config = sp.GetRequiredService<WorkspaceConfig>();
            var models = sp.GetRequiredService<ModelClientFactory>();
            var builder = sp.GetService<ToolsetBuilder>();

            // 子智能体拿「免审批」的工具面：子任务的审批不透传父 agent，包了只会永远挂起。
            var candidates = builder?.Build(ToolProfile.Work, requireApproval: false) ?? Toolset.Empty;
            var mcpNames = sp.GetService<McpToolset>()?.Tools
                .OfType<AIFunction>()
                .Select(static tool => tool.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            IChatClient? subagentClient = null;
            var subagents = SubagentFactory.Build(
                config.Tools.Subagents,
                candidates.Tools,
                mcpNames,
                () => subagentClient ??= models.CreateMain(),
                sp.GetService<INotificationPusher>(),
                sp.GetService<ILoggerFactory>()?.CreateLogger("Shoyee.Agent.Subagents"));

            // 压缩默认关；只有显式打开才去建摘要模型，缺 small 档就整体放弃（不退化截断）。
            // 窗口/输出上限优先取模型配置（DeepSeek 1M/384K、MiMo 1M/128K），没填才用压缩段默认值。
            var compaction = (config.Context ?? new ContextOptions()).EffectiveCompaction with
            {
                MaxContextWindowTokens = config.Models.Main.ContextLength ?? CompactionOptions.Default.MaxContextWindowTokens,
                MaxOutputTokens = config.Models.Main.MaxOutputTokens ?? CompactionOptions.Default.MaxOutputTokens,
            };
            IChatClient? compactionSummarizer = null;
            if (compaction.Enabled)
            {
                try
                {
                    compactionSummarizer = models.CreateSmall();
                }
                catch (Exception ex)
                {
                    sp.GetService<ILoggerFactory>()
                        ?.CreateLogger("Shoyee.Agent.Compaction")
                        .LogWarning(ex, "小模型不可用，工作模式上下文压缩保持关闭");
                }
            }

            return new WorkAgentFactory(
                models.CreateMain(),
                config.Root,
                toolset: builder?.Build(ToolProfile.Work) ?? Toolset.Empty,
                subagents: subagents,
                subagentOptions: config.Tools.Subagents,
                compaction: compaction,
                compactionSummarizer: compactionSummarizer);
        });
        services.TryAddSingleton(static sp => sp.GetRequiredService<WorkAgentFactory>().CreateAgent());
        services.TryAddSingleton(static sp => new WorkSessionCache(
            sp.GetRequiredService<HarnessAgent>(),
            new ShoyeeAgentSessionStore(sp.GetRequiredService<ISessionStateRepository>())));
        services.TryAddSingleton<WorkTurnRunner>();

        services.TryAddSingleton(static sp => new RoleplayAgentFactory(
            sp.GetRequiredService<ModelClientFactory>().CreateMain(),
            sp.GetService<ToolsetBuilder>()?.Build(ToolProfile.Roleplay).Tools));
        services.TryAddSingleton(static sp =>
        {
            var recorder = new RoundRecorder(sp.GetRequiredService<IRoundRepository>());
            var agent = sp.GetRequiredService<RoleplayAgentFactory>().CreateAgent(recorder);
            return new RoleplayTurnRunner(
                agent,
                sp.GetRequiredService<RoleplayContextBuilder>(),
                recorder,
                sp.GetRequiredService<RoleplaySummarizer>(),
                sp.GetRequiredService<IMessageRepository>());
        });

        // 入站附件自动分析：视觉端点 + 转写能力都从 DI 探测，缺哪个就跳过哪个方向
        services.TryAddSingleton<IAttachmentAnalyzer>(static sp => AttachmentAnalyzer.Create(
            sp.GetService<WorkspaceConfig>(),
            sp.GetService<ModelClientFactory>(),
            sp.GetService<Shoyee.Voice.ISpeechTranscriber>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<AttachmentAnalyzer>>()));

        // 没有 Gateway 时事件无人接收；Gateway 注册自己的 sink 后覆盖
        services.TryAddSingleton<ITurnEventSink, NullTurnEventSink>();
        services.TryAddSingleton<ApprovalBroker>();
        services.TryAddSingleton<IApprovalConsole>(static sp => sp.GetRequiredService<ApprovalBroker>());
        // 后台进程完成回执：送回原会话（桌面走事件，IM 按 Lane 的 last_target 回贴）
        services.TryAddSingleton<IProcessCompletionSink>(static sp => new SessionProcessCompletionSink(
            new Lazy<ITurnService>(() => sp.GetRequiredService<ITurnService>()),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ILaneRepository>(),
            sp.GetRequiredService<ILogger<SessionProcessCompletionSink>>()));
        services.TryAddSingleton<ISessionService, SessionService>();
        // TurnService 同时是 ITurnService（业务入口）与 ITurnDrain（停机收尾），必须是同一个实例
        services.TryAddSingleton<TurnService>();
        services.TryAddSingleton<ITurnService>(static sp => sp.GetRequiredService<TurnService>());
        services.TryAddSingleton<ITurnDrain>(static sp => sp.GetRequiredService<TurnService>());
        return services;
    }
}
