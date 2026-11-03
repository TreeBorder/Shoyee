using Microsoft.Extensions.AI;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Tools.Mcp;

namespace Shoyee.Tools.Composition;

/// <summary>
/// <c>tools.list</c> 的实现：把组装器里的工具描述与 MCP 当前状态合成一份清单。
/// 禁用名单来自当前生效配置；MCP 用重连后的最新状态（<see cref="McpManager.Status"/>）。
/// </summary>
public sealed class ToolCatalog : IToolCatalog
{
    private readonly ToolsetBuilder _builder;
    private readonly McpManager? _mcp;

    /// <summary>创建清单。</summary>
    /// <param name="builder">工具组装器。</param>
    /// <param name="mcp">MCP 管理器（未配置 MCP 时为 null）。</param>
    public ToolCatalog(ToolsetBuilder builder, McpManager? mcp = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
        _mcp = mcp;
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolInfo> List()
    {
        var disabled = _builder.DisabledTools;
        var items = new Dictionary<string, ToolInfo>(StringComparer.Ordinal);

        foreach (var descriptor in _builder.DescribeAll())
        {
            var metadata = descriptor.Metadata;
            items[metadata.Name] = new ToolInfo(
                metadata.Name,
                Describe(descriptor.Tool),
                ScopeName(metadata.Scope),
                !disabled.Contains(metadata.Name),
                metadata.RequiresApproval,
                metadata.Writes,
                "builtin");
        }

        if (_mcp is not null)
        {
            var descriptions = _mcp.Current.Tools
                .OfType<AIFunction>()
                .Where(static tool => !string.IsNullOrWhiteSpace(tool.Name))
                .ToDictionary(static tool => tool.Name!, Describe, StringComparer.Ordinal);

            foreach (var server in _mcp.Status)
            {
                foreach (var name in server.Tools)
                {
                    items[name] = new ToolInfo(
                        name,
                        descriptions.GetValueOrDefault(name, string.Empty),
                        "work",
                        server.Connected && !disabled.Contains(name),
                        RequiresApproval: false,
                        Writes: false,
                        Source: $"mcp:{server.Name}");
                }
            }
        }

        return items.Values
            .OrderBy(static item => item.Source, StringComparer.Ordinal)
            .ThenBy(static item => item.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static string Describe(AITool tool)
        => tool is AIFunction function ? function.Description ?? string.Empty : string.Empty;

    private static string ScopeName(ToolScope scope) => scope switch
    {
        ToolScope.Work => "work",
        ToolScope.Roleplay => "roleplay",
        ToolScope.Channel => "channel",
        _ => "all",
    };
}
