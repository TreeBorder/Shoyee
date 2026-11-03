using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Rpc;

namespace Shoyee.Gateway.Subscriptions;

/// <summary>
/// 事件总线：<see cref="ITurnEventSink"/> 的实现，按连接角色 + Lane 过滤后广播。
/// 过滤是服务端强制的——桌面 RP 只收 RP Lane 的事件（可以跨 Lane 切换查看），工作 Lane 一律不推。
/// 同时充当 <see cref="IClientPresence"/>：连接表就是在线状态，主动消息据此选投递目标。
/// </summary>
public sealed class EventHub : ITurnEventSink, IClientPresence
{
    private readonly ConcurrentDictionary<Guid, RpcServerConnection> _connections = new();
    private readonly ILogger<EventHub> _logger;
    private volatile ImmutableHashSet<string> _roleplayLanes = ImmutableHashSet<string>.Empty;

    /// <summary>创建事件总线。</summary>
    /// <param name="logger">日志。</param>
    public EventHub(ILogger<EventHub>? logger = null)
    {
        _logger = logger ?? NullLogger<EventHub>.Instance;
    }

    /// <summary>当前连接数。</summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>登记 RP Lane（用于把事件分成「RP」与「工作」两路）。</summary>
    /// <param name="laneIds">RP 模式的 Lane id。</param>
    public void SetRoleplayLanes(IEnumerable<string> laneIds)
        => _roleplayLanes = laneIds.ToImmutableHashSet(StringComparer.Ordinal);

    /// <summary>该 Lane 是否 RP Lane。</summary>
    /// <param name="laneId">Lane id。</param>
    public bool IsRoleplayLane(string? laneId)
        => laneId is not null && _roleplayLanes.Contains(laneId);

    /// <summary>
    /// 该角色当前是否有已握手的连接。只数已经 <see cref="Attach"/> 过的连接，
    /// 所以握手中/握手中断的客户端不算在线（心跳不该投给一个还没订阅上事件的连接）。
    /// </summary>
    /// <param name="role">客户端角色。</param>
    public bool IsOnline(ClientRole role)
        => _connections.Values.Any(connection => connection.State.Role == role);

    /// <summary>推一条事件给所有有权限的连接。</summary>
    /// <param name="eventName">事件名。</param>
    /// <param name="payload">载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async ValueTask PublishAsync(
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(payload);

        var laneId = LaneOf(payload);
        var fromRoleplayLane = IsRoleplayLane(laneId);
        var paramsElement = JsonSerializer.SerializeToElement(payload, payload.GetType(), JsonWire.Options);

        foreach (var connection in _connections.Values)
        {
            if (!connection.ShouldReceive(eventName, laneId, fromRoleplayLane))
            {
                continue;
            }

            try
            {
                await connection.SendEventAsync(eventName, paramsElement, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "事件推送失败：{Event} → {Connection}", eventName, connection.State.DisplayName);
            }
        }
    }

    internal void Attach(RpcServerConnection connection) => _connections[connection.Id] = connection;

    internal void Detach(Guid connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>关闭全部连接（停机时最后一步）。</summary>
    public async Task CloseAllAsync()
    {
        var connections = _connections.Values.ToList();
        _connections.Clear();
        foreach (var connection in connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string? LaneOf(object payload) => payload switch
    {
        TurnStartedEvent e => e.LaneId,
        TurnDeltaEvent e => e.LaneId,
        TurnThinkingEvent e => e.LaneId,
        TurnToolEvent e => e.LaneId,
        TurnCompletedEvent e => e.LaneId,
        TurnFailedEvent e => e.LaneId,
        ApprovalRequestDto e => e.LaneId,
        ApprovalResolvedDto e => e.LaneId,
        MessageAppendedEvent e => e.LaneId,
        SessionRenamedEvent e => e.LaneId,
        Shoyee.Contracts.Dtos.PhotoReadyEvent e => e.LaneId,
        _ => null,
    };
}
