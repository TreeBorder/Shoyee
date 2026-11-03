using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Options;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Daemon.Lanes;

/// <summary>
/// 启动时把配置里的账号播种成 <c>lanes</c> 行。
///
/// - 只写静态字段（channel / mode / external_key / display_name）；
/// - 保留 <c>active_session_id</c>、<c>last_inbound_at</c>、<c>last_interaction_at</c>、<c>created_at</c>；
/// - 配置里移除的旧 Lane **不删**（历史会话仍要能指回它）。
/// </summary>
public static class LaneSeeder
{
    /// <summary>按配置算出应有的 Lane（不含运行时状态）。</summary>
    /// <param name="config">工作区配置。</param>
    /// <param name="now">首次创建时间。</param>
    public static IReadOnlyList<LaneRecord> Build(WorkspaceConfig config, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(config);

        var lanes = new List<LaneRecord>
        {
            // 桌面 RP 永远存在：桌面本身就是一条 Lane
            new(
                LaneIds.DesktopRoleplay,
                LaneChannel.Desktop,
                Mode.Roleplay,
                "desktop",
                "桌面",
                ActiveSessionId: null,
                LastInboundAt: null,
                LastInteractionAt: null,
                now),
        };

        if (config.Channels.Mattermost is { } mattermost)
        {
            lanes.Add(ToLane(mattermost.Roleplay, Mode.Roleplay, now));
            lanes.Add(ToLane(mattermost.Work, Mode.Work, now));
        }

        if (config.Channels.WeChat is { } weChat)
        {
            lanes.Add(new LaneRecord(
                LaneIds.For(LaneChannel.WeChat, Mode.Work),
                LaneChannel.WeChat,
                Mode.Work,
                "wechat",
                weChat.DisplayName ?? "微信",
                ActiveSessionId: null,
                LastInboundAt: null,
                LastInteractionAt: null,
                now));
        }

        return lanes;
    }

    /// <summary>读现有行、合并静态字段后写回。</summary>
    /// <param name="lanes">Lane 仓库。</param>
    /// <param name="config">工作区配置。</param>
    /// <param name="clock">时间源（测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<IReadOnlyList<LaneRecord>> EnsureLanesAsync(
        ILaneRepository lanes,
        WorkspaceConfig config,
        TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var result = new List<LaneRecord>();
        foreach (var desired in Build(config, now))
        {
            var existing = await lanes.GetAsync(desired.Id, cancellationToken).ConfigureAwait(false);
            var merged = existing is null
                ? desired
                : desired with
                {
                    ActiveSessionId = existing.ActiveSessionId,
                    LastInboundAt = existing.LastInboundAt,
                    LastInteractionAt = existing.LastInteractionAt,
                    CreatedAt = existing.CreatedAt,
                    LastTarget = existing.LastTarget,
                };
            result.Add(await lanes.UpsertAsync(merged, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    private static LaneRecord ToLane(MattermostAccount account, Mode mode, DateTimeOffset now) => new(
        LaneIds.For(LaneChannel.Mattermost, mode),
        LaneChannel.Mattermost,
        mode,
        account.BaseUrl,
        account.DisplayName ?? (mode == Mode.Roleplay ? "Mattermost · 角色" : "Mattermost · 工作"),
        ActiveSessionId: null,
        LastInboundAt: null,
        LastInteractionAt: null,
        now);
}
