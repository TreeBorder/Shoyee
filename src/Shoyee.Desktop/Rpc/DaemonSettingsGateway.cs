using System.Text.Json;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Desktop.Rpc;

/// <summary>用工作连接实现设置页的只读取数。</summary>
public sealed class DaemonSettingsGateway : ISettingsGateway
{
    private readonly DaemonConnection _connection;

    /// <summary>创建网关。</summary>
    /// <param name="connection">daemon 连接。</param>
    public DaemonSettingsGateway(DaemonConnection connection) => _connection = connection;

    /// <inheritdoc />
    public bool IsConnected => _connection.IsConnected;

    /// <inheritdoc />
    public Task<JsonElement> GetConfigAsync(CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<JsonElement>(RpcMethods.ConfigGet, null, cancellationToken);

    /// <inheritdoc />
    public Task<WorkspacePathsDto> GetWorkspacePathsAsync(CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<WorkspacePathsDto>(RpcMethods.WorkspacePaths, null, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolInfo>> ListToolsAsync(
        string? scope = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _connection.CallWorkAsync<ToolListResult>(
                RpcMethods.ToolsList,
                new ToolListRequest(scope, query),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpServerStatus>> GetMcpStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _connection.CallWorkAsync<McpStatusResult>(
                RpcMethods.McpStatus,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Servers;
    }

    /// <inheritdoc />
    public Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<StatusDto>(RpcMethods.StatusGet, null, cancellationToken);

    /// <inheritdoc />
    public Task<StateDto> SetStateAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<StateDto>(
            RpcMethods.StateSet,
            new StateSetRequest(key, value),
            cancellationToken);

    /// <inheritdoc />
    public Task<ConfigReloadResult> ReloadConfigAsync(CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<ConfigReloadResult>(RpcMethods.ConfigReload, null, cancellationToken);

    /// <inheritdoc />
    public Task<ConfigSetResult> SetConfigAsync(
        System.Text.Json.Nodes.JsonObject patch,
        CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<ConfigSetResult>(
            RpcMethods.ConfigSet,
            new ConfigSetRequest(Patch: patch),
            cancellationToken);

    /// <inheritdoc />
    public Task<McpReloadResult> ReloadMcpAsync(CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<McpReloadResult>(RpcMethods.McpReload, null, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexEntry>> ListIndexAsync(
        string? scope = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _connection.CallWorkAsync<IndexListResult>(
                RpcMethods.IndexList,
                new IndexListRequest(scope, query),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Items;
    }

    /// <inheritdoc />
    public Task<IndexEntry> UpsertIndexAsync(IndexEntry entry, CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<IndexEntry>(
            RpcMethods.IndexUpsert,
            new IndexUpsertRequest(entry),
            cancellationToken);

    /// <inheritdoc />
    public Task RemoveIndexAsync(string key, CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<JsonElement>(
            RpcMethods.IndexRemove,
            new IndexRemoveRequest(key),
            cancellationToken);

    /// <inheritdoc />
    public Task RestartDaemonAsync(CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<JsonElement>(RpcMethods.DaemonRestart, null, cancellationToken);
}
