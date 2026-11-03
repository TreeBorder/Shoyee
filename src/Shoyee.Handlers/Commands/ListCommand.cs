using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// <c>/list</c>：列出本 Lane 的工作会话（含已归档），带序号供 <c>/resume</c> 使用。
/// 对话模式只有一条时间线，没有会话可列。
/// </summary>
public sealed class ListCommand : ICommandHandler
{
    /// <summary>列表上限：IM 一条消息放得下，也够 <c>/resume 序号</c> 用。</summary>
    internal const int MaxRows = 20;

    private readonly ISessionService _sessions;

    /// <summary>创建指令。</summary>
    /// <param name="sessions">工作会话服务。</param>
    public ListCommand(ISessionService sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <inheritdoc />
    public string CommandName => "list";

    /// <inheritdoc />
    public string Description => "会话列表";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (context.Mode != Mode.Work)
        {
            return "🙅 对话模式只有一条时间线，没有会话可列。";
        }

        var page = await _sessions
            .ListAsync(
                new SessionListRequest(Mode.Work, context.LaneId, IncludeArchived: true, Limit: MaxRows),
                cancellationToken)
            .ConfigureAwait(false);
        if (page.Items.Count == 0)
        {
            return "📭 还没有会话，先聊一句或 /new。";
        }

        var rows = page.Items
            .Select((session, index) => new[]
            {
                session.Id == context.TargetId ? $"⭐ {index + 1}" : (index + 1).ToString(),
                TableText.Cell(session.Title ?? "未命名") + (session.Archived ? "（已归档）" : string.Empty),
                TableText.Time(session.LastActiveAt),
                session.MessageCount.ToString(),
            })
            .ToList();

        return "📋 会话\n"
               + TableText.Table(["#", "会话", "最后活跃", "消息"], rows)
               + "\n🔎 详情：/resume 序号切回 · /title 新标题改名 · /new 开新会话";
    }
}
