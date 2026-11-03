using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Shoyee.Sessions.Persistence;

namespace Shoyee.Agent.Runtime.Work;

/// <summary>
/// 工作模式的常驻 session 缓存：内存在跑，落盘只发生在边界。
///
/// - 命中内存时直接返回**同一个 <see cref="AgentSession"/> 实例**，零 IO、零序列化；
/// - 只有未命中（进程重启 / 被 <see cref="Evict"/> / 轮转后）才从 <c>session_state</c> 反序列化一次；
/// - <see cref="SaveAsync"/> 由回合末调用一次，整包写盘用于崩溃恢复——**不是每次 Get 都写**。
///
/// 按 sessionId 加锁，避免同一会话并发装载/覆盖。
/// </summary>
public sealed class WorkSessionCache
{
    private readonly AIAgent _agent;
    private readonly ShoyeeAgentSessionStore _store;
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <summary>创建缓存。</summary>
    /// <param name="agent">工作模式 agent。</param>
    /// <param name="store">session 持久化。</param>
    public WorkSessionCache(AIAgent agent, ShoyeeAgentSessionStore store)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(store);
        _agent = agent;
        _store = store;
    }

    /// <summary>把新建的 session 放进内存（首次会话创建后调用）。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="session">框架 session。</param>
    public void Put(string sessionId, AgentSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(session);
        _sessions[sessionId] = session;
    }

    /// <summary>取 session：内存命中直接返回同一实例（零 IO）；未命中才反序列化并缓存。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AgentSession> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        => await FindAsync(sessionId, cancellationToken).ConfigureAwait(false)
           ?? throw new InvalidOperationException($"会话 {sessionId} 没有持久化状态，无法 resume。");

    /// <summary>同 <see cref="GetAsync"/>，但状态缺失时返回 null（供领域服务转成明确的 RPC 错误）。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (_sessions.TryGetValue(sessionId, out var cached))
        {
            return cached;
        }

        var gate = _gates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue(sessionId, out cached))
            {
                return cached;
            }

            var loaded = await _store
                .GetSessionAsync(_agent, new AgentSessionStoreKey(sessionId), cancellationToken)
                .ConfigureAwait(false);
            if (loaded is null)
            {
                return null;
            }

            _sessions[sessionId] = loaded;
            return loaded;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>把内存里的 session 整包写入存储（回合末调用一次，用于崩溃恢复）。</summary>
    /// <param name="sessionId">会话 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }

        await _store.SaveSessionAsync(_agent, new AgentSessionStoreKey(sessionId), session, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>把会话踢出内存（归档/轮转后调用）；不影响已落盘内容。</summary>
    /// <param name="sessionId">会话 id。</param>
    public bool Evict(string sessionId) => _sessions.TryRemove(sessionId, out _);
}
