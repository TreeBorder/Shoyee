using System.Text.Json;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Desktop.Rpc;

/// <summary>用 <see cref="DaemonConnection"/> 的工作连接实现工作台 RPC 面。</summary>
public sealed class DaemonWorkChatGateway : IWorkChatGateway
{
    private readonly DaemonConnection _connection;

    /// <summary>创建网关。</summary>
    /// <param name="connection">daemon 连接。</param>
    public DaemonWorkChatGateway(DaemonConnection connection)
    {
        _connection = connection;
        _connection.WorkEventReceived += OnWorkEvent;
    }

    /// <inheritdoc />
    public bool IsConnected => _connection.IsConnected;

    /// <inheritdoc />
    public event Action<string, JsonElement>? EventReceived;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var page = await _connection.CallWorkAsync<PageResult<SessionSummary>>(
                RpcMethods.SessionList,
                new SessionListRequest(Mode.Work, Limit: 100),
                cancellationToken)
            .ConfigureAwait(false);
        return page.Items;
    }

    /// <inheritdoc />
    public Task<SessionMessagesResult> GetMessagesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<SessionMessagesResult>(
            RpcMethods.SessionMessages,
            new SessionMessagesRequest(sessionId, Limit: 200),
            cancellationToken);

    /// <inheritdoc />
    public Task<SessionDetail> CreateSessionAsync(
        string? title = null,
        CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<SessionDetail>(
            RpcMethods.SessionCreate,
            new SessionCreateRequest(Mode.Work, Title: title),
            cancellationToken);

    /// <inheritdoc />
    public async Task<string> SendTurnAsync(
        string sessionId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var turn = await _connection.CallWorkAsync<TurnRef>(
                RpcMethods.TurnSend,
                new TurnSendRequest(text, SessionId: sessionId),
                cancellationToken)
            .ConfigureAwait(false);
        return turn.TurnId;
    }

    /// <inheritdoc />
    public Task RespondApprovalAsync(
        string approvalId,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<JsonElement>(
            RpcMethods.ApprovalRespond,
            new ApprovalRespondRequest(approvalId, decision),
            cancellationToken);

    /// <inheritdoc />
    public Task CancelTurnAsync(
        string sessionId,
        string? turnId = null,
        CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<JsonElement>(
            RpcMethods.TurnCancel,
            new TurnCancelRequest(sessionId, turnId),
            cancellationToken);

    /// <inheritdoc />
    public Task<TtsSynthesizeResult> SynthesizeSpeechAsync(
        string text,
        CancellationToken cancellationToken = default)
        => _connection.CallWorkAsync<TtsSynthesizeResult>(
            RpcMethods.TtsSynthesize,
            new TtsSynthesizeRequest(Text: text),
            cancellationToken);

    private void OnWorkEvent(string name, JsonElement payload) => EventReceived?.Invoke(name, payload);
}
