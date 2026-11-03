using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// <c>/resume [序号|id前缀]</c>：把本 Lane 的当前会话切回旧会话。
///
/// 先 <c>OpenAsync</c>（校验并装进常驻缓存）再切指针——打不开就保持原指针。
/// 归档会话不能切回（消息仍在，只是不能再续聊）。
/// </summary>
public sealed class ResumeCommand : ICommandHandler
{
    private readonly ISessionService _sessions;

    /// <summary>创建指令。</summary>
    /// <param name="sessions">工作会话服务。</param>
    public ResumeCommand(ISessionService sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <inheritdoc />
    public string CommandName => "resume";

    /// <inheritdoc />
    public string Description => "切回会话";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (context.Mode != Mode.Work)
        {
            return "🙅 对话模式只有一条时间线，不需要切换会话。";
        }

        var argument = context.Arguments.Trim();
        if (argument.Length == 0)
        {
            return "⌨️ 用法：/resume 序号（先 /list 看）或 /resume 会话 id 前缀";
        }

        var page = await _sessions
            .ListAsync(
                new SessionListRequest(Mode.Work, context.LaneId, IncludeArchived: true, Limit: ListCommand.MaxRows),
                cancellationToken)
            .ConfigureAwait(false);

        SessionSummary? target;
        if (int.TryParse(argument, out var index))
        {
            if (index < 1 || index > page.Items.Count)
            {
                return $"🙅 没有第 {index} 个会话，先 /list 看看。";
            }

            target = page.Items[index - 1];
        }
        else
        {
            target = page.Items.FirstOrDefault(
                session => session.Id.StartsWith(argument, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return $"🙅 找不到 id 以 {TableText.Cell(argument)} 开头的会话。";
            }
        }

        if (target.Archived)
        {
            return "📦 这个会话已归档，切不回去（消息还在，但不能接着聊）。";
        }

        try
        {
            await _sessions.OpenAsync(target.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            return "⚠️ 打不开这个会话：" + ex.Message;
        }

        await _sessions.SetActiveForLaneAsync(target.Id, context.LaneId, cancellationToken).ConfigureAwait(false);
        return $"🔁 已切回：{TableText.Cell(target.Title ?? "未命名")}\n🔎 详情：{target.Id}";
    }
}
