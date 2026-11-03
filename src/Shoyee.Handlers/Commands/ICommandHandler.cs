namespace Shoyee.Handlers.Commands;

/// <summary>
/// 预置指令处理器（每个指令一个类）。
///
/// 指令是消息管线的**确定性入口**：命中就由代码执行，绝不进模型；没命中的 <c>/xxx</c>
/// 当普通消息放行（聊天里打出一个路径不该被当成指令）。
/// </summary>
public interface ICommandHandler
{
    /// <summary>指令名（不含斜杠），如 <c>approve</c>。</summary>
    string CommandName { get; }

    /// <summary>一句话说明（<c>/help</c> 表格「说明」列用；尽量短，参数写法放命令自己的用法提示里）。</summary>
    string Description { get; }

    /// <summary>执行指令，返回要回给用户的文本（空串 = 不回复）。</summary>
    /// <param name="context">指令上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default);
}
