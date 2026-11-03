using System.Text.Json;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Desktop.Rpc;

/// <summary>用 <see cref="DaemonConnection"/> 的角色模式连接实现 RP 的 RPC 面。</summary>
public sealed class DaemonRoleplayChatGateway : IRoleplayChatGateway
{
    private readonly DaemonConnection _connection;

    /// <summary>创建网关。</summary>
    /// <param name="connection">daemon 连接。</param>
    public DaemonRoleplayChatGateway(DaemonConnection connection)
    {
        _connection = connection;
        _connection.RoleplayEventReceived += OnRoleplayEvent;
    }

    /// <inheritdoc />
    public bool IsConnected => _connection.IsRoleplayConnected;

    /// <inheritdoc />
    public event Action<string, JsonElement>? EventReceived;

    /// <inheritdoc />
    public Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
        => _connection.CallRoleplayAsync<StatusDto>(RpcMethods.StatusGet, null, cancellationToken);

    /// <inheritdoc />
    public Task<SessionMessagesResult> GetLaneMessagesAsync(
        string laneId,
        long? beforeId = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
        => _connection.CallRoleplayAsync<SessionMessagesResult>(
            RpcMethods.LaneMessages,
            new LaneMessagesRequest(laneId, BeforeId: beforeId, Limit: limit),
            cancellationToken);

    /// <inheritdoc />
    public async Task<string> SendTurnAsync(
        string laneId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var turn = await _connection.CallRoleplayAsync<TurnRef>(
                RpcMethods.TurnSend,
                new TurnSendRequest(text, LaneId: laneId),
                cancellationToken)
            .ConfigureAwait(false);
        return turn.TurnId;
    }

    /// <inheritdoc />
    public Task RespondApprovalAsync(
        string approvalId,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default)
        => _connection.CallRoleplayAsync<JsonElement>(
            RpcMethods.ApprovalRespond,
            new ApprovalRespondRequest(approvalId, decision),
            cancellationToken);

    /// <inheritdoc />
    public Task CancelTurnAsync(
        string laneId,
        string? turnId = null,
        CancellationToken cancellationToken = default)
        => _connection.CallRoleplayAsync<JsonElement>(
            RpcMethods.TurnCancel,
            new TurnCancelRequest(laneId, turnId),
            cancellationToken);

    /// <inheritdoc />
    public Task<TtsSynthesizeResult> SynthesizeSpeechAsync(
        string text,
        CancellationToken cancellationToken = default)
        => _connection.CallRoleplayAsync<TtsSynthesizeResult>(
            RpcMethods.TtsSynthesize,
            new TtsSynthesizeRequest(Text: text),
            cancellationToken);

    private void OnRoleplayEvent(string name, JsonElement payload) => EventReceived?.Invoke(name, payload);
}
