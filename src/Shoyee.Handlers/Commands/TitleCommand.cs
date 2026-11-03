using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Handlers.Commands;

/// <summary><c>/title 新标题</c>：给本 Lane 的当前会话改名（列表里认人用）。</summary>
public sealed class TitleCommand : ICommandHandler
{
    private readonly ISessionService _sessions;

    /// <summary>创建指令。</summary>
    /// <param name="sessions">工作会话服务。</param>
    public TitleCommand(ISessionService sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <inheritdoc />
    public string CommandName => "title";

    /// <inheritdoc />
    public string Description => "会话改名";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (context.Mode != Mode.Work)
        {
            return "🙅 对话模式没有会话，改不了名。";
        }

        var title = context.Arguments.Trim();
        if (title.Length == 0)
        {
            return "⌨️ 用法：/title 新标题";
        }

        try
        {
            await _sessions.RenameAsync(context.TargetId, title, cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            return "⚠️ 改名失败：" + ex.Message;
        }

        return $"✏️ 已改名：{TableText.Cell(title)}";
    }
}
