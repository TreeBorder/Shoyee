using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// <c>/state [key]</c>：只读查看角色状态表（心情/精力/思念等）。
///
/// 状态是**事实数据**，必须确定性读取——问智能体会得到角色口吻的回答（「人家有点困呢」），
/// 不是 <c>energy=72</c>。写入口只有桌面设置与她的 <c>update_state</c> 工具，不在 IM 开第三条。
/// </summary>
public sealed class StateCommand : ICommandHandler
{
    private readonly IStateRepository _states;

    /// <summary>创建指令。</summary>
    /// <param name="states">状态仓库。</param>
    public StateCommand(IStateRepository states)
    {
        ArgumentNullException.ThrowIfNull(states);
        _states = states;
    }

    /// <inheritdoc />
    public string CommandName => "state";

    /// <inheritdoc />
    public string Description => "角色状态";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        if (context.Mode != Mode.Roleplay)
        {
            return "🙅 角色状态只在角色模式有意义。";
        }

        var key = context.Arguments.Trim();
        if (key.Length > 0)
        {
            var single = await _states.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (single is null)
            {
                return $"🙅 没有状态 {TableText.Cell(key)}，/state 看全部。";
            }

            return TableText.Table(
                ["状态", "值", "说明", "更新"],
                [new[] { TableText.Cell(single.Name), TableText.Cell(single.Value), TableText.Cell(single.Description), TableText.Time(single.UpdatedAt) }]);
        }

        var all = await _states.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (all.Count == 0)
        {
            return "📭 还没有角色状态。";
        }

        var rows = all
            .OrderBy(static state => state.Key, StringComparer.Ordinal)
            .Select(static state => new[]
            {
                TableText.Cell(state.Name),
                TableText.Cell(state.Value),
                TableText.Cell(state.Description),
            })
            .ToList();

        return "💗 角色状态\n" + TableText.Table(["状态", "值", "说明"], rows);
    }
}
