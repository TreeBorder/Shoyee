using Shoyee.Handlers.Commands;

namespace Shoyee.Handlers;

/// <summary>
/// 指令路由：解析 <c>/cmd args</c> 并分发到处理器。入站管线在进 agent 回合前先问
/// <see cref="IsKnownCommand"/>——只拦**已注册**的指令，未知的 <c>/xxx</c> 当普通消息。
/// </summary>
public sealed class CommandRouter
{
    private readonly Dictionary<string, ICommandHandler> _handlers;

    /// <summary>创建路由器。</summary>
    /// <param name="handlers">已注册的指令处理器。</param>
    public CommandRouter(IEnumerable<ICommandHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(static handler => handler.CommandName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>全部已注册指令（<c>/help</c> 用）。</summary>
    public IReadOnlyCollection<ICommandHandler> Handlers => _handlers.Values;

    /// <summary>这条消息是否命中已注册的指令。</summary>
    /// <param name="text">用户原文。</param>
    public bool IsKnownCommand(string text)
        => !string.IsNullOrWhiteSpace(text) && _handlers.ContainsKey(ParseCommandName(text));

    /// <summary>解析指令名（不含斜杠与参数）；不是指令返回空串。</summary>
    /// <param name="text">用户原文。</param>
    public static string ParseCommandName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('/'))
        {
            return string.Empty;
        }

        var body = trimmed[1..];
        var spaceIndex = body.IndexOfAny([' ', '\t', '\n', '\r']);
        return spaceIndex < 0 ? body : body[..spaceIndex];
    }

    /// <summary>解析参数（指令名之后的部分，已 trim）。</summary>
    /// <param name="text">用户原文。</param>
    public static string ParseArguments(string text)
    {
        if (!IsCommand(text))
        {
            return string.Empty;
        }

        var trimmed = text.TrimStart()[1..];
        var spaceIndex = trimmed.IndexOfAny([' ', '\t', '\n', '\r']);
        return spaceIndex < 0 ? string.Empty : trimmed[(spaceIndex + 1)..].Trim();
    }

    /// <summary>这条消息是否以斜杠开头（不论是否注册）。</summary>
    /// <param name="text">用户原文。</param>
    public static bool IsCommand(string text)
        => !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('/');

    /// <summary>路由并执行。</summary>
    /// <param name="context">上下文（参数从 <paramref name="text"/> 里解）。</param>
    /// <param name="text">用户原文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>要回给用户的文本。</returns>
    /// <exception cref="InvalidOperationException">指令未注册（调用方应先用 <see cref="IsKnownCommand"/> 判断）。</exception>
    public Task<string> RouteAsync(
        CommandContext context,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = ParseCommandName(text);
        if (!_handlers.TryGetValue(name, out var handler))
        {
            throw new InvalidOperationException($"未注册的指令：/{name}");
        }

        return handler.HandleAsync(context with { Arguments = ParseArguments(text) }, cancellationToken);
    }
}
