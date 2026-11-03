using System.Text.Json;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Desktop.Rpc;

/// <summary>用工作连接实现定时任务页的 RPC 面。</summary>
public sealed class DaemonCronGateway : ICronGateway
{
    private readonly DaemonConnection _connection;

    /// <summary>创建网关。</summary>
    /// <param name="connection">daemon 连接。</param>
    public DaemonCronGateway(DaemonConnection connection) => _connection = connection;

    /// <inheritdoc />
    public bool IsConnected => _connection.IsConnected;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CronJobDto>> ListAsync(
        bool includeDisabled = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _connection.CallWorkAsync<CronListResult>(
                RpcMethods.CronList,
                new CronListRequest(includeDisabled),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Items;
    }

    /// <inheritdoc />
    public Task<CronJobDetail> GetAsync(
        string jobId,
        int executions = 10,
        CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<CronJobDetail>(
            RpcMethods.CronGet,
            new CronGetRequest(jobId, executions),
            cancellationToken);

    /// <inheritdoc />
    public Task SetEnabledAsync(string jobId, bool enabled, CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<JsonElement>(
            RpcMethods.CronSetEnabled,
            new CronSetEnabledRequest(jobId, enabled),
            cancellationToken);

    /// <inheritdoc />
    public Task RemoveAsync(string jobId, CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<JsonElement>(
            RpcMethods.CronRemove,
            new CronRemoveRequest(jobId),
            cancellationToken);
}
