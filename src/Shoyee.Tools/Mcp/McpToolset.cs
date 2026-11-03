using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Tools.Mcp;

/// <summary>MCP 工具集：连接持有者 + 工具列表 + 每服务状态；空集用 <see cref="Empty"/>。</summary>
public sealed class McpToolset : IAsyncDisposable
{
    private readonly IReadOnlyList<McpClient> _clients;

    /// <summary>空工具集（未配置 MCP 时）。</summary>
    public static McpToolset Empty { get; } = new([], [], [], []);

    internal McpToolset(
        IReadOnlyList<McpClient> clients,
        IReadOnlyList<AITool> tools,
        IReadOnlyList<string> failures,
        IReadOnlyList<McpServerStatus> servers)
    {
        _clients = clients;
        Tools = tools;
        Failures = failures;
        Servers = servers;
    }

    /// <summary>全部 MCP 工具（已消重名），直接进 <c>ChatOptions.Tools</c>。</summary>
    public IReadOnlyList<AITool> Tools { get; }

    /// <summary>连接失败的服务（「名：错误」）——个别服务不可用不拖垮启动。</summary>
    public IReadOnlyList<string> Failures { get; }

    /// <summary>每个配置过的服务的连接状态与工具清单。</summary>
    public IReadOnlyList<McpServerStatus> Servers { get; }

    /// <summary>断开全部 MCP 连接。</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
