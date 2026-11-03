using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Proactive.Heartbeat;

/// <summary>
/// 心跳落在哪条 Lane：**桌面在线就桌面（当面聊），否则退回最近互动的 RP IM**。
///
/// 只选 Lane，不产生消息——心跳仍然跑一个真轮次，消息走正常的 round/messages 落库，
/// 在线的桌面由 <c>turn.*</c> 事件看到，离线的桌面重连后拉 <c>lane.messages</c>。
/// 所以这里不需要「桌面出站发送器」，Lane 选对了投递就自然成立。
/// </summary>
public sealed class HeartbeatTargetSelector
{
    private readonly ILaneRepository _lanes;
    private readonly IClientPresence _presence;

    /// <summary>创建选择器。</summary>
    /// <param name="lanes">Lane 仓库。</param>
    /// <param name="presence">客户端在线判定。</param>
    public HeartbeatTargetSelector(ILaneRepository lanes, IClientPresence presence)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(presence);
        _lanes = lanes;
        _presence = presence;
    }

    /// <summary>
    /// 选出这轮心跳的 Lane。
    ///
    /// 顺序：桌面 RP 在线 → 桌面 Lane；否则取「有回投地址（<c>last_target</c>）的 RP IM Lane」中
    /// 最近互动的一条。都没有（桌面离线且从没有过 RP IM 入站）→ <c>null</c>，这轮不发。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<LaneRecord?> SelectAsync(CancellationToken cancellationToken = default)
    {
        var lanes = await _lanes.ListAsync(Mode.Roleplay, cancellationToken).ConfigureAwait(false);

        var desktop = lanes.FirstOrDefault(static lane => lane.Channel == LaneChannel.Desktop);
        if (desktop is not null && _presence.IsOnline(ClientRole.DesktopRoleplay))
        {
            return desktop;
        }

        return lanes
            .Where(static lane => lane.Channel != LaneChannel.Desktop && !string.IsNullOrEmpty(lane.LastTarget))
            .OrderByDescending(LastSeen)
            .ThenBy(static lane => lane.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>最近互动时间；从没互动过就用建 Lane 的时间（避免一次都没聊过的 Lane 抢到目标）。</summary>
    private static DateTimeOffset LastSeen(LaneRecord lane)
        => lane.LastInteractionAt ?? lane.LastInboundAt ?? lane.CreatedAt;
}
