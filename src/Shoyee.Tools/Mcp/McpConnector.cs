using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;

namespace Shoyee.Tools.Mcp;

/// <summary>
/// MCP 客户端连接器：官方 <c>ModelContextProtocol</c> 包，<c>McpClient.ListToolsAsync()</c> 的
/// <c>McpClientTool</c>（= <see cref="AIFunction"/>）直接进 <c>ChatOptions.Tools</c>。
/// 工具一律加服务名前缀（<c>squad_</c> / <c>rook_</c>），防跨服务撞名与冒名本地保留名。
/// 单个服务连接失败只记 <see cref="McpToolset.Failures"/>，不炸启动。
/// </summary>
public static class McpConnector
{
    /// <summary>本地保留工具名（MCP 同名工具必须改名，防审批白名单被冒名）。</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        Files.FileAccessTools.ReadToolName,
        Files.FileAccessTools.ReadLinesToolName,
        Files.FileAccessTools.LsToolName,
        Files.FileAccessTools.GrepToolName,
        Files.FileAccessTools.WriteToolName,
        Files.FileAccessTools.DeleteToolName,
        Files.FileAccessTools.ReplaceToolName,
        Files.FileAccessTools.ReplaceLinesToolName,
        Shell.ShellTool.ToolName,
        Process.ProcessTool.StartToolName,
        Process.ProcessTool.StatusToolName,
        Process.ProcessTool.KillToolName,
        Memory.HistoryTool.SearchToolName,
        Memory.HistoryTool.ReadToolName,
        Notes.NotesTools.SearchToolName,
        Notes.NotesTools.ReadToolName,
        Notes.NotesTools.WriteToolName,        Memory.IndexTool.ListToolName,
        Memory.IndexTool.AddToolName,
        Memory.IndexTool.UpdateToolName,
        Memory.IndexTool.RemoveToolName,
        Media.ImageAnalysisTool.ToolName,
        Web.WebTool.SearchToolName,
        Web.WebTool.FetchToolName,
        State.StateTool.GetStatesToolName,
        State.StateTool.GetStateToolName,
        State.StateTool.UpdateStateToolName,
        State.StateTool.UpdateStatesToolName,
        State.StateTool.AddStateToolName,
    };

    /// <summary>按配置连接全部启用的 MCP 服务并收罗工具。</summary>
    /// <param name="config">工作区配置（读 <see cref="WorkspaceConfig.Tools"/> 的 MCP 段）。</param>
    /// <param name="transportFactory">测试可注入传输工厂。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<McpToolset> ConnectAsync(
        WorkspaceConfig config,
        Func<McpServerConfig, IClientTransport>? transportFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var servers = config.Tools.Mcp ?? [];
        var clients = new List<McpClient>();
        var tools = new List<AITool>();
        var failures = new List<string>();
        var statuses = new List<McpServerStatus>(servers.Count);
        var usedNames = new HashSet<string>(ReservedNames, StringComparer.Ordinal);

        foreach (var server in servers)
        {
            if (!server.Enabled || string.IsNullOrWhiteSpace(server.Url))
            {
                statuses.Add(new McpServerStatus(
                    server.Name, server.Url, server.Enabled, Connected: false, Tools: [], Error: null));
                continue;
            }

            var serverTools = new List<string>();
            try
            {
                var transport = transportFactory?.Invoke(server) ?? CreateTransport(server);
                var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                clients.Add(client);

                foreach (var tool in await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    if (server.AllowedTools is { Count: > 0 }
                        && !server.AllowedTools.Contains(tool.Name, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    var prefix = string.IsNullOrWhiteSpace(server.Name) ? "mcp" : server.Name.Trim();
                    var name = $"{prefix}_{tool.Name}";
                    if (!usedNames.Add(name))
                    {
                        name = $"{name}_{usedNames.Count}";
                        usedNames.Add(name);
                    }

                    tools.Add(tool.WithName(name));
                    serverTools.Add(name);
                }

                statuses.Add(new McpServerStatus(
                    server.Name, server.Url, server.Enabled, Connected: true, serverTools, Error: null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{server.Name}: {ex.Message}");
                statuses.Add(new McpServerStatus(
                    server.Name, server.Url, server.Enabled, Connected: false, serverTools, ex.Message));
            }
        }

        return new McpToolset(clients, tools, failures, statuses);
    }

    /// <summary>按配置创建 Streamable HTTP 传输；token 从环境变量读、作 Bearer 头。</summary>
    public static IClientTransport CreateTransport(McpServerConfig server)
    {
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(server.TokenEnv)
            && Environment.GetEnvironmentVariable(server.TokenEnv) is { Length: > 0 } token)
        {
            headers["Authorization"] = "Bearer " + token;
        }

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(server.Url),
            Name = server.Name,
            TransportMode = HttpTransportMode.AutoDetect,
            AdditionalHeaders = headers.Count == 0 ? null : headers,
        });
    }
}
