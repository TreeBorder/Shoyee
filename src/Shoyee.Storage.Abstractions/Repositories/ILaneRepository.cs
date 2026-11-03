using Shoyee.Contracts.Domain;

namespace Shoyee.Storage.Abstractions;

/// <summary>Lane 仓库：渠道账号状态与当前会话指针。</summary>
public interface ILaneRepository
{
    /// <summary>全部 Lane（可按模式过滤）。</summary>
    Task<IReadOnlyList<LaneRecord>> ListAsync(Mode? mode = null, CancellationToken cancellationToken = default);

    /// <summary>按 id 取。</summary>
    Task<LaneRecord?> GetAsync(string laneId, CancellationToken cancellationToken = default);

    /// <summary>新增或覆盖（启动时按配置同步）。</summary>
    Task<LaneRecord> UpsertAsync(LaneRecord lane, CancellationToken cancellationToken = default);

    /// <summary>设置当前工作会话指针（null = 清空）。</summary>
    Task SetActiveSessionAsync(string laneId, string? sessionId, CancellationToken cancellationToken = default);

    /// <summary>记一次入站（同时刷新最近互动时间与回投地址）。</summary>
    /// <param name="laneId">Lane id。</param>
    /// <param name="at">入站时刻。</param>
    /// <param name="target">该条消息的回投地址（渠道自定义的不透明串；桌面 Lane 传 null）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task TouchInboundAsync(
        string laneId,
        DateTimeOffset at,
        string? target = null,
        CancellationToken cancellationToken = default);

    /// <summary>记一次互动（任意模式、任意渠道）。</summary>
    Task TouchInteractionAsync(string laneId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>全局最近互动时间（心跳/思念的唯一依据）。</summary>
    Task<DateTimeOffset?> GetLastInteractionAsync(CancellationToken cancellationToken = default);
}
