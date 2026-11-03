using Microsoft.Extensions.AI;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Sessions.Persistence;

/// <summary>
/// 工作模式转录投影：把会话状态里的框架历史增量抄进 <c>messages</c>（检索/列表/导出用）。
///
/// 真源是 <c>session_state</c>，这里只写派生行——抄失败不影响会话本身，重抄一遍也不会重复。
/// 定位靠「历史下标」（写入 <c>source_index</c>，同会话同下标唯一），不依赖框架消息自带的 id：
/// 实测 <see cref="ChatMessage.MessageId"/> 在 10.10.1 里默认为 null，用户输入/工具结果都不保证有值。
///
/// 前提：历史只追加。将来若真的压缩/替换历史（长度变短或换内容），必须调用
/// <see cref="RebuildAsync"/>（或至少让长度变短触发自动重抄），否则下标会错位。
/// </summary>
public sealed class SessionTranscript
{
    private readonly IMessageRepository _messages;

    /// <summary>创建投影器。</summary>
    /// <param name="messages">消息投影仓库。</param>
    public SessionTranscript(IMessageRepository messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = messages;
    }

    /// <summary>
    /// 增量补抄：从上次抄到的位置往后写。历史比投影还短（被压缩/替换）时自动整段重建。
    /// </summary>
    /// <param name="laneId">会话所属 Lane。</param>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="history">当前完整历史（真源里的顺序）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次新增的投影行；没有新增返回空。</returns>
    public async Task<IReadOnlyList<MessageRecord>> SyncAsync(
        string laneId,
        string sessionId,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(history);

        var cursor = await _messages.GetProjectedIndexAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (cursor > history.Count)
        {
            // 真源变短 = 历史被压缩/替换，旧下标不再对位；整段重抄而不是继续往后错
            await _messages.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            cursor = 0;
        }

        if (cursor >= history.Count)
        {
            return [];
        }

        var pending = history.Skip(cursor).ToList();
        return await _messages
            .AppendRangeAsync(laneId, sessionId, pending, cursor, at: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>从真源整段重建投影（删掉旧行后全量重抄）。</summary>
    /// <param name="laneId">会话所属 Lane。</param>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="history">当前完整历史。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重建后的投影行。</returns>
    public async Task<IReadOnlyList<MessageRecord>> RebuildAsync(
        string laneId,
        string sessionId,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(history);

        await _messages.DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (history.Count == 0)
        {
            return [];
        }

        return await _messages
            .AppendRangeAsync(laneId, sessionId, history, 0, at: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
