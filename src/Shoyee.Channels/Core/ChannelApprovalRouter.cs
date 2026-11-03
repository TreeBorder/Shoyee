using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shoyee.Channels.Delivery;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Channels.Core;

/// <summary>
/// 审批的 IM 旁路：轮次挂起等审批时，把提示发到该 Lane 的回投地址，用户用
/// <c>/approve</c> <c>/reject</c>（由 <see cref="Handlers.CommandRouter"/> 截住）答复。
///
/// 只对**非桌面 Lane** 且**有 <c>last_target</c>** 的发：桌面发起的轮次自己看
/// <c>approval.request</c> 事件；没有回投地址的 Lane 发了也到不了人。
/// 一批里可能有多条 <c>approval.request</c>（一次多个工具调用），按审批 id 去重，
/// 第一次出现时把整批合成一条提示。
/// </summary>
public sealed class ChannelApprovalRouter : ITurnEventSink
{
    private const int AnnouncedLimit = 2048;

    private readonly ReliableDelivery _delivery;
    private readonly ILaneRepository _lanes;
    private readonly IServiceProvider _services;
    private readonly IReadOnlyDictionary<string, IChannelAdapter> _adapters;
    private readonly ILogger<ChannelApprovalRouter> _logger;
    private readonly ConcurrentDictionary<string, byte> _announced = new(StringComparer.Ordinal);

    /// <summary>创建审批旁路。</summary>
    /// <param name="delivery">出站投递（带台账）。</param>
    /// <param name="lanes">Lane 仓库（取回投地址）。</param>
    /// <param name="services">服务容器（延迟取审批台，避免审批台→事件出口→本类的构造环）。</param>
    /// <param name="adapters">渠道适配器（停 typing）。</param>
    /// <param name="logger">日志。</param>
    public ChannelApprovalRouter(
        ReliableDelivery delivery,
        ILaneRepository lanes,
        IServiceProvider services,
        IEnumerable<IChannelAdapter> adapters,
        ILogger<ChannelApprovalRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(logger);
        _delivery = delivery;
        _lanes = lanes;
        _services = services;
        _adapters = adapters.ToDictionary(static a => a.LaneId, StringComparer.Ordinal);
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync(
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (eventName == RpcEvents.ApprovalResolved && payload is ApprovalResolvedDto resolved)
        {
            _announced.TryRemove(resolved.ApprovalId, out _);
            return;
        }

        if (eventName != RpcEvents.ApprovalRequest || payload is not ApprovalRequestDto request)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.LaneId))
        {
            return;
        }

        // 同一批的多条请求只提示一次：取会话挂起的整批，只看还没提示过的
        var approvals = _services.GetRequiredService<IApprovalConsole>();
        var pending = approvals.Peek(request.SessionId);
        var fresh = pending.Where(item => !_announced.ContainsKey(item.ApprovalId)).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        var lane = await _lanes.GetAsync(request.LaneId, cancellationToken).ConfigureAwait(false);
        if (lane is null || lane.Channel == LaneChannel.Desktop || string.IsNullOrWhiteSpace(lane.LastTarget))
        {
            return;
        }

        foreach (var item in fresh)
        {
            _announced[item.ApprovalId] = 0;
        }

        if (_announced.Count > AnnouncedLimit)
        {
            _announced.Clear();
        }

        var target = lane.LastTarget;
        await StopTypingAsync(lane.Id, target, cancellationToken).ConfigureAwait(false);

        // RP 的事件 sessionId 是 Lane id 占位；工作模式才是真会话
        var sessionId = string.Equals(request.SessionId, lane.Id, StringComparison.Ordinal)
            ? null
            : request.SessionId;

        await _delivery
            .SendTextAsync(lane.Id, sessionId, target, ApprovalPrompt.Format(fresh), kind: "approval", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "审批提示已发到 {LaneId} / {Target}（{Count} 项）",
            lane.Id,
            target,
            fresh.Count);
    }

    private async Task StopTypingAsync(string laneId, string target, CancellationToken cancellationToken)
    {
        if (!_adapters.TryGetValue(laneId, out var adapter) || adapter is not ITypingChannel typing)
        {
            return;
        }

        try
        {
            await typing.StopTypingAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "停止 typing 失败：{LaneId}", laneId);
        }
    }
}
