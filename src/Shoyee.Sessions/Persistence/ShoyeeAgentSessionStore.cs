using System.Text.Json;
using Microsoft.Agents.AI;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Sessions.Persistence;

/// <summary>
/// 框架 <see cref="AgentSessionStore"/> 的持久化实现：JSON 原文存 <c>session_state</c>。
///
/// 存的不是「状态片段」，而是 <see cref="AIAgent.SerializeSessionAsync"/> 的整个产物——
/// 工作模式的完整历史、开场那条 system、压缩点、审批残留都在里面；读回直接交给 agent 反序列化，
/// 不在这里解析、不改写任何字段。RP 不用这个 store（它每轮从 rounds 重建）。
/// </summary>
public sealed class ShoyeeAgentSessionStore : AgentSessionStore
{
    private readonly ISessionStateRepository _repository;

    /// <summary>创建存储。</summary>
    /// <param name="repository">会话状态仓库。</param>
    public ShoyeeAgentSessionStore(ISessionStateRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc />
    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(session);

        var json = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _repository.SaveAsync(StorageKey(key), json.GetRawText(), DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(key);

        var json = await _repository.GetAsync(StorageKey(key), cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        // 每次读都重新反序列化 → 天然返回独立副本（框架要求同 key 的并发分支互不影响）
        return await agent.DeserializeSessionAsync(document.RootElement.Clone(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 存储键 = <c>sessionId</c>，有分区时追加 <c>|k=v&amp;k=v</c>（按名字序数排序）。
    /// 分区属于会话身份的一部分，不能只按 SessionId 存，否则同 id 的不同维度会互相覆盖。
    /// </summary>
    private static string StorageKey(AgentSessionStoreKey key)
    {
        if (key.Partitions is not { Count: > 0 } partitions)
        {
            return key.SessionId;
        }

        var parts = partitions
            .OrderBy(static p => p.Key, StringComparer.Ordinal)
            .Select(static p => p.Key + "=" + p.Value);
        return key.SessionId + "|" + string.Join('&', parts);
    }
}
