using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// <c>/new</c>：开一个新工作会话，重读人设文件，并把该 Lane 的当前会话指针切过去。
/// 对话模式只有一条时间线，没有会话概念，直接回绝。
/// </summary>
public sealed class NewCommand : ICommandHandler
{
    private readonly ISessionService _sessions;

    /// <summary>创建指令。</summary>
    /// <param name="sessions">工作会话服务。</param>
    public NewCommand(ISessionService sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <inheritdoc />
    public string CommandName => "new";

    /// <inheritdoc />
    public string Description => "开新会话（重读人设）";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (context.Mode != Mode.Work)
        {
            return "🙅 对话模式只有一条时间线，不需要 /new；想换话题直接说就行。";
        }

        var session = await _sessions
            .CreateAsync(new SessionCreateRequest(Mode.Work, context.LaneId), cancellationToken)
            .ConfigureAwait(false);
        await _sessions
            .SetActiveForLaneAsync(session.Id, context.LaneId, cancellationToken)
            .ConfigureAwait(false);

        return $"✅ 开了一个新会话：{session.Title ?? session.Id}";
    }
}
