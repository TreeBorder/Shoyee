using Shoyee.Contracts.Abstractions;

namespace Shoyee.Handlers.Commands;

/// <summary><c>/stop</c>：停就停——取消当前会话正在跑的那一轮（已落盘内容不回滚）。</summary>
public sealed class StopCommand : ICommandHandler
{
    private readonly ITurnService _turns;

    /// <summary>创建指令。</summary>
    /// <param name="turns">轮次服务。</param>
    public StopCommand(ITurnService turns)
    {
        ArgumentNullException.ThrowIfNull(turns);
        _turns = turns;
    }

    /// <inheritdoc />
    public string CommandName => "stop";

    /// <inheritdoc />
    public string Description => "停下当前";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        await _turns.CancelAsync(context.TargetId, turnId: null, cancellationToken).ConfigureAwait(false);
        return "⏹ 好，我停。";
    }
}
