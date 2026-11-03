using Microsoft.Extensions.AI;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Storage.Abstractions;

/// <summary>消息投影仓库：检索、列表、导出。数据可从真源重建。</summary>
public interface IMessageRepository
{
    /// <summary>追加一条消息投影；不可见/无内容的返回 null。</summary>
    Task<MessageRecord?> AppendAsync(
        string laneId,
        string? sessionId,
        int? roundNo,
        ChatMessage message,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量追加工作模式的转录投影（一个事务，正文同步进 FTS）。
    /// <paramref name="startIndex"/> 是第一条消息在会话历史里的下标，逐条递增记录到 <c>source_index</c>。
    /// 同会话同下标有唯一约束：重复写会报唯一冲突，不会出现第二行。
    /// </summary>
    Task<IReadOnlyList<MessageRecord>> AppendRangeAsync(
        string laneId,
        string sessionId,
        IReadOnlyList<ChatMessage> messages,
        int startIndex,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default);

    /// <summary>工作模式已抄到的历史位置（最后一条投影的下标 + 1；没有为 0）。</summary>
    Task<int> GetProjectedIndexAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取最后一条「可见助手正文」投影（<c>turn.completed</c> 的 messageId 用它）。
    /// 可按 Lane / 会话 / 轮次过滤；工具调用与空正文不算。
    /// </summary>
    Task<MessageRecord?> GetLastAssistantAsync(
        string? laneId = null,
        string? sessionId = null,
        int? roundNo = null,
        CancellationToken cancellationToken = default);

    /// <summary>删除某会话的全部投影（含 FTS 行），返回删除条数。用于从真源整段重建。</summary>
    Task<int> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把附件合并到一条工具结果投影（异步结果回写用），按 Lane + 工具返回文本里的标记定位。
    /// 同一个文件重复回写不会产生两行附件。没找到返回 null。
    /// </summary>
    /// <param name="laneId">所属 Lane。</param>
    /// <param name="resultMarker">出现在工具返回文本里的任务标记（如异步出图的任务 id）。</param>
    /// <param name="attachments">要合并的附件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<MessageRecord?> AttachToToolResultAsync(
        string laneId,
        string resultMarker,
        IReadOnlyList<MessageAttachment> attachments,
        CancellationToken cancellationToken = default);

    /// <summary>按 id 取单条投影；不存在返回 null。</summary>
    Task<MessageRecord?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>按 id 游标分页（升序）。</summary>
    Task<IReadOnlyList<MessageRecord>> GetPageAsync(
        string? laneId = null,
        string? sessionId = null,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>统计条数。</summary>
    Task<int> CountAsync(string? laneId = null, string? sessionId = null, CancellationToken cancellationToken = default);
}
