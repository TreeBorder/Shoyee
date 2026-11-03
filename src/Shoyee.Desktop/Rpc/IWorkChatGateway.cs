using System.Text.Json;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Desktop.Rpc;

/// <summary>
/// 工作台需要的最小 RPC 面。抽成接口是为了让 <c>WorkViewModel</c> 不直接依赖 socket，
/// 测试里可以塞一个假的网关。
/// </summary>
public interface IWorkChatGateway
{
    /// <summary>当前是否连着 daemon。</summary>
    bool IsConnected { get; }

    /// <summary>服务端事件（事件名，载荷）；回调在读取线程上，订阅方自己切 UI 线程。</summary>
    event Action<string, JsonElement>? EventReceived;

    /// <summary>工作会话列表（按最后活跃倒序）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>取一个会话的消息（按 id 升序，最多 200 条）。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SessionMessagesResult> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>新建工作会话。</summary>
    /// <param name="title">标题；null = 默认。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SessionDetail> CreateSessionAsync(string? title = null, CancellationToken cancellationToken = default);

    /// <summary>发一轮，返回 turnId。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="text">正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string> SendTurnAsync(string sessionId, string text, CancellationToken cancellationToken = default);

    /// <summary>答复审批。</summary>
    /// <param name="approvalId">审批 id。</param>
    /// <param name="decision">结论。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RespondApprovalAsync(
        string approvalId,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>取消当前轮。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="turnId">轮次 id；null = 该会话当轮。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CancelTurnAsync(string sessionId, string? turnId = null, CancellationToken cancellationToken = default);

    /// <summary>把一段正文合成语音（桌面「播放」按钮）。</summary>
    /// <param name="text">要朗读的正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TtsSynthesizeResult> SynthesizeSpeechAsync(
        string text,
        CancellationToken cancellationToken = default);
}
