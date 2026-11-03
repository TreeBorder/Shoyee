using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Sessions.Roleplay;
using Shoyee.Tools.Composition;

namespace Shoyee.Agent.Composition;

/// <summary>
/// RP agent 工厂：普通 <see cref="ChatClientAgent"/>，不用框架历史。
///
/// 每轮由 <c>RoleplayContextBuilder</c> 组装完整消息列表；<see cref="RoundRecorder"/>
/// （AIContextProvider）负责把本轮响应存下来、轮末落库——这就是 Harem 的轮消息 provider。
/// 工具面只上只读工具（普通 agent 没有审批中间件）。
/// </summary>
public sealed class RoleplayAgentFactory
{
    /// <summary>RP agent 的稳定 id。</summary>
    public const string AgentId = "shoyee-roleplay";

    private readonly IChatClient _main;
    private readonly IReadOnlyList<AITool>? _tools;

    /// <summary>创建工厂。</summary>
    /// <param name="main">主模型客户端。</param>
    /// <param name="tools">工具面（null = 不带工具）。</param>
    public RoleplayAgentFactory(IChatClient main, IReadOnlyList<AITool>? tools = null)
    {
        ArgumentNullException.ThrowIfNull(main);
        _main = main;
        _tools = tools;
    }

    /// <summary>创建 RP agent。</summary>
    /// <param name="recorder">本轮响应记录中间件。</param>
    public ChatClientAgent CreateAgent(RoundRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        return new ChatClientAgent(_main, new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = "Shoyee",
            ChatOptions = new ChatOptions { Tools = _tools?.ToList() },
            // 这两行只是接线：轮消息捕获在 RoundRecorder，组装在 RoleplayContextBuilder，
            // 落库/窗口在 IRoundRepository，30 轮摘要触发在 RoleplaySummarizer。
            // 框架拿 null 时默认塞 InMemoryChatHistoryProvider；RP 的历史只从 rounds 重建，
            // 显式换成空实现，别让框架悄悄攒第二份真相。轮消息由 recorder 记。
            ChatHistoryProvider = NoHistoryProvider.Instance,
            AIContextProviders = [recorder],
        });
    }

    private sealed class NoHistoryProvider : ChatHistoryProvider
    {
        public static readonly NoHistoryProvider Instance = new();

        protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
            InvokingContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IEnumerable<ChatMessage>>([]);

        protected override ValueTask StoreChatHistoryAsync(
            InvokedContext context,
            CancellationToken cancellationToken = default)
            => default;
    }
}
