using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Composition;

namespace Shoyee.Tools.State;

/// <summary>
/// RP 状态工具族 <c>get_states</c> / <c>get_state</c> / <c>update_state</c> / <c>update_states</c> / <c>add_state</c>
/// （对应 Harem <c>StateTool</c>）：她通过它维护心情/精力等状态。
/// <c>update_*</c> 走 <c>byAgent=true</c>，受 <see cref="StateRecord.AgentEditable"/> 限制；
/// <c>add_state</c> 新增的状态默认可由她编辑。
/// </summary>
public static class StateTool
{
    /// <summary>读取全部状态工具名。</summary>
    public const string GetStatesToolName = "get_states";

    /// <summary>读取单个状态工具名。</summary>
    public const string GetStateToolName = "get_state";

    /// <summary>更新单个状态工具名。</summary>
    public const string UpdateStateToolName = "update_state";

    /// <summary>批量更新状态工具名。</summary>
    public const string UpdateStatesToolName = "update_states";

    /// <summary>新增状态工具名。</summary>
    public const string AddStateToolName = "add_state";

    /// <summary>构建五个状态工具。</summary>
    /// <param name="states">状态仓库。</param>
    /// <param name="events">事件出口；非 null 时每次改值都推 <c>state.updated</c>（桌面实时回显）。</param>
    public static IReadOnlyList<ToolDescriptor> Create(IStateRepository states, ITurnEventSink? events = null)
    {
        ArgumentNullException.ThrowIfNull(states);

        var getAll = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken = default) =>
            {
                var all = await states.GetAllAsync(cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Serialize(
                    all.ToDictionary(
                        static s => s.Key,
                        static s => new { s.Name, s.Value, s.Description, s.UpdatedAt }));
            },
            new AIFunctionFactoryOptions
            {
                Name = GetStatesToolName,
                Description = "获取全部角色状态（key/名称/值/说明/更新时间）。",
            });

        var get = AIFunctionFactory.Create(
            async ([Description("状态 key（如 mood / energy / longing）。")] string key, CancellationToken cancellationToken = default) =>
            {
                var state = await states.GetAsync(key, cancellationToken).ConfigureAwait(false);
                return state is null
                    ? JsonSerializer.Serialize(new { Message = $"状态 {key} 不存在。" })
                    : JsonSerializer.Serialize(new { state.Name, state.Value, state.Description, state.UpdatedAt });
            },
            new AIFunctionFactoryOptions
            {
                Name = GetStateToolName,
                Description = "获取单个角色状态。",
            });

        var update = AIFunctionFactory.Create(
            async (
                [Description("状态 key。")] string key,
                [Description("新的值（字符串；数值状态自己保持数字格式）。")] string value,
                CancellationToken cancellationToken = default) =>
            {
                var ok = await states.SetAsync(key, value, byAgent: true, cancellationToken).ConfigureAwait(false);
                if (ok)
                {
                    await PublishAsync(states, events, key, cancellationToken).ConfigureAwait(false);
                }

                return ok ? $"状态 {key} 已更新为 {value}。" : $"状态 {key} 更新失败：不存在或不允许她修改。";
            },
            new AIFunctionFactoryOptions
            {
                Name = UpdateStateToolName,
                Description = "更新单个角色状态（受「她可改」限制）。",
            });

        var updateMany = AIFunctionFactory.Create(
            async (
                [Description("要批量更新的状态：key → 新值。")] Dictionary<string, string> values,
                CancellationToken cancellationToken = default) =>
            {
                var changed = new List<string>();
                var rejected = new List<string>();
                foreach (var (key, value) in values)
                {
                    if (await states.SetAsync(key, value, byAgent: true, cancellationToken).ConfigureAwait(false))
                    {
                        changed.Add(key);
                        await PublishAsync(states, events, key, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        rejected.Add(key);
                    }
                }

                var text = changed.Count > 0 ? $"已更新：{string.Join("、", changed)}。" : "没有状态被更新。";
                return rejected.Count > 0
                    ? text + $"拒绝/不存在：{string.Join("、", rejected)}。"
                    : text;
            },
            new AIFunctionFactoryOptions
            {
                Name = UpdateStatesToolName,
                Description = "批量更新角色状态（一次改多个 key）。",
            });

        var add = AIFunctionFactory.Create(
            async (
                [Description("状态 key（唯一）。")] string key,
                [Description("展示名。")] string name,
                [Description("初始值。")] string value,
                [Description("给模型的说明与取值约束。")] string description,
                CancellationToken cancellationToken = default) =>
            {
                if (await states.GetAsync(key, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return $"状态 {key} 已存在，要改值用 {UpdateStateToolName}。";
                }

                await states.UpsertAsync(
                        new StateRecord(key, name, value, description, true, DateTimeOffset.UtcNow),
                        cancellationToken)
                    .ConfigureAwait(false);
                await PublishAsync(states, events, key, cancellationToken).ConfigureAwait(false);
                return $"状态 {key} 已添加。";
            },
            new AIFunctionFactoryOptions
            {
                Name = AddStateToolName,
                Description = "新增一个角色状态（默认她可修改）。已有状态要改值用 update_state。",
            });

        return
        [
            ToolDescriptor.Create(getAll, ToolScope.Roleplay, writes: false, requiresApproval: false),
            ToolDescriptor.Create(get, ToolScope.Roleplay, writes: false, requiresApproval: false),
            ToolDescriptor.Create(update, ToolScope.Roleplay, writes: true, requiresApproval: false),
            ToolDescriptor.Create(updateMany, ToolScope.Roleplay, writes: true, requiresApproval: false),
            ToolDescriptor.Create(add, ToolScope.Roleplay, writes: true, requiresApproval: false),
        ];
    }

    /// <summary>改完值后推一条完整状态（客户端不用再拉 status.get）。</summary>
    public static async Task PublishAsync(
        IStateRepository states,
        ITurnEventSink? events,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (events is null)
        {
            return;
        }

        var record = await states.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        await events.PublishAsync(
                RpcEvents.StateUpdated,
                new StateDto(
                    record.Key,
                    record.Name,
                    record.Value,
                    record.Description,
                    record.AgentEditable,
                    record.UpdatedAt),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
