using Microsoft.Extensions.DependencyInjection;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// <c>/help</c>：用表格列出全部指令。
///
/// 延迟解析 <see cref="CommandRouter"/>：构造时直接注入会和
/// 「CommandRouter ← IEnumerable&lt;ICommandHandler&gt; ← 本类」成环（DI 拒绝）。
/// </summary>
public sealed class HelpCommand : ICommandHandler
{
    private readonly IServiceProvider _services;

    /// <summary>创建指令。</summary>
    /// <param name="services">服务容器（只为延迟取路由器）。</param>
    public HelpCommand(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    public string CommandName => "help";

    /// <inheritdoc />
    public string Description => "本帮助";

    /// <inheritdoc />
    public Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var rows = _services.GetRequiredService<CommandRouter>().Handlers
            .OrderBy(static handler => handler.CommandName, StringComparer.Ordinal)
            .Select(static handler => new[] { "`/" + handler.CommandName + "`", TableText.Cell(handler.Description) })
            .ToList();
        return Task.FromResult("📚 可用指令\n" + TableText.Table(["指令", "说明"], rows));
    }
}
