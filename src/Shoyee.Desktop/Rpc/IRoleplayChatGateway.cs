using System.Text.Json;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Desktop.Rpc;

/// <summary>
/// 角色模式需要的最小 RPC 面。抽成接口是为了让 <c>RoleplayViewModel</c> 不直接依赖 socket，
/// 测试里可以塞一个假的网关。
///
/// 桌面 RP 只写 <c>desktop:roleplay</c>；其它 RP Lane 只读，靠 <see cref="GetLaneMessagesAsync"/> 拉历史。
/// </summary>
public interface IRoleplayChatGateway
{
    /// <summary>角色模式连接是否可用。</summary>
    bool IsConnected { get; }

    /// <summary>服务端事件（事件名，载荷）；回调在读取线程上，订阅方自己切 UI 线程。</summary>
    event Action<string, JsonElement>? EventReceived;

    /// <summary>运行状态：RP Lane 列表 + 全局状态（心情/思念等）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>取某条 RP Lane 的消息（按 id 升序）。</summary>
    /// <param name="laneId">Lane id。</param>
    /// <param name="beforeId">取该消息 id 之前的一页。</param>
    /// <param name="limit">本页条数（1–200）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SessionMessagesResult> GetLaneMessagesAsync(
        string laneId,
        long? beforeId = null,
        int limit = 200,
        CancellationToken cancellationToken = default);

    /// <summary>往 RP Lane 发一轮，返回 turnId。</summary>
    /// <param name="laneId">目标 Lane（桌面固定 <c>desktop:roleplay</c>）。</param>
    /// <param name="text">正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string> SendTurnAsync(string laneId, string text, CancellationToken cancellationToken = default);

    /// <summary>答复审批。</summary>
    /// <param name="approvalId">审批 id。</param>
    /// <param name="decision">结论。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RespondApprovalAsync(
        string approvalId,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>取消该 Lane 当前轮。</summary>
    /// <param name="laneId">Lane id。</param>
    /// <param name="turnId">轮次 id；null = 该 Lane 当轮。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CancelTurnAsync(string laneId, string? turnId = null, CancellationToken cancellationToken = default);

    /// <summary>把一段正文合成语音（桌面「播放」按钮）。</summary>
    /// <param name="text">要朗读的正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TtsSynthesizeResult> SynthesizeSpeechAsync(
        string text,
        CancellationToken cancellationToken = default);
}
