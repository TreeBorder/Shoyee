using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Composition;
using Shoyee.Agent.Runtime.Work;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Sessions.Lifecycle;
using Shoyee.Sessions.Persona;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Sessions;

/// <summary>
/// <see cref="ISessionService"/> 的实现：把「会话元数据」（Sessions）与「框架 session 运行时」（Agent）接起来。
///
/// 分工：读操作、改名、归档、分页全部交给 <see cref="SessionCatalog"/>；
/// 只有 <see cref="CreateAsync"/> 与 <see cref="OpenAsync"/> 需要 Agent——建框架 session、装进常驻缓存。
/// </summary>
public sealed class SessionService : ISessionService
{
    private readonly SessionCatalog _catalog;
    private readonly SessionFactory _factory;
    private readonly WorkAgentFactory _agentFactory;
    private readonly HarnessAgent _agent;
    private readonly WorkSessionCache _cache;
    private readonly PersonaFiles _persona;
    private readonly ILaneRepository _lanes;
    private readonly IIndexRepository? _index;

    /// <summary>创建会话服务。</summary>
    /// <param name="catalog">会话目录（读操作与元数据）。</param>
    /// <param name="factory">会话记录工厂。</param>
    /// <param name="agentFactory">工作模式 agent 工厂（建框架 session）。</param>
    /// <param name="agent">工作模式 agent（历史 provider 必须与工厂一致）。</param>
    /// <param name="cache">常驻 session 缓存。</param>
    /// <param name="persona">人设文件读取。</param>
    /// <param name="lanes">Lane 仓库。</param>
    /// <param name="index">INDEX 仓库（可空 = 不注入 INDEX 概览）。</param>
    public SessionService(
        SessionCatalog catalog,
        SessionFactory factory,
        WorkAgentFactory agentFactory,
        HarnessAgent agent,
        WorkSessionCache cache,
        PersonaFiles persona,
        ILaneRepository lanes,
        IIndexRepository? index = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(lanes);
        _catalog = catalog;
        _factory = factory;
        _agentFactory = agentFactory;
        _agent = agent;
        _cache = cache;
        _persona = persona;
        _lanes = lanes;
        _index = index;
    }

    /// <inheritdoc />
    public Task<PageResult<SessionSummary>> ListAsync(
        SessionListRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.ListAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<SessionDetail> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        => _catalog.GetAsync(sessionId, cancellationToken);

    /// <inheritdoc />
    public async Task<SessionDetail> OpenAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var detail = await _catalog.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (detail.Archived)
        {
            throw new RpcException(RpcErrors.SessionArchived, "会话已归档，不能继续对话", new { sessionId });
        }

        if (detail.Mode != Mode.Work)
        {
            throw new RpcException(RpcErrors.InvalidParams, "RP 没有会话，历史请用 lane.messages", new { sessionId });
        }

        var session = await _cache.FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            // 元数据在、session_state 丢了：不是「会话不存在」，而是数据不完整，别让客户端去重试创建
            throw new RpcException(
                RpcErrors.Internal,
                "会话状态缺失，无法 resume",
                new { sessionId, reason = "state_missing" });
        }

        return detail;
    }

    /// <inheritdoc />
    public async Task<SessionDetail> CreateAsync(
        SessionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode != Mode.Work)
        {
            throw new RpcException(RpcErrors.InvalidParams, "RP 没有会话，不能 session.create");
        }

        LaneRecord? lane = null;
        if (!string.IsNullOrWhiteSpace(request.LaneId))
        {
            lane = await _lanes.GetAsync(request.LaneId, cancellationToken).ConfigureAwait(false)
                   ?? throw new RpcException(RpcErrors.NotFound, "Lane 不存在", new { laneId = request.LaneId });
            if (lane.Mode != Mode.Work)
            {
                throw new RpcException(
                    RpcErrors.InvalidParams, "该 Lane 不是工作模式", new { laneId = request.LaneId });
            }
        }

        var prompt = await BuildSystemPromptAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new RpcException(RpcErrors.Internal, "工作区人设文件为空，无法创建工作会话");
        }

        var record = await _factory
            .CreateAsync(
                sessionKey: null,
                origin: lane?.Channel ?? LaneChannel.Desktop,
                mode: Mode.Work,
                laneId: lane?.Id,
                title: request.Title,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var session = await _agentFactory.CreateSessionAsync(_agent, prompt, cancellationToken).ConfigureAwait(false);
        _cache.Put(record.Id, session);

        // 立刻落一份：否则进程在首轮前退出，这个会话就永远 open 不了
        await _cache.SaveAsync(record.Id, cancellationToken).ConfigureAwait(false);

        return await _catalog.GetAsync(record.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken = default)
        => _catalog.RenameAsync(sessionId, title, cancellationToken);

    /// <inheritdoc />
    public async Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _catalog.ArchiveAsync(sessionId, cancellationToken).ConfigureAwait(false);
        _cache.Evict(sessionId);
    }

    /// <inheritdoc />
    public Task SetActiveForLaneAsync(
        string sessionId,
        string laneId,
        CancellationToken cancellationToken = default)
        => _catalog.SetActiveForLaneAsync(sessionId, laneId, cancellationToken);

    /// <inheritdoc />
    public Task<SessionMessagesResult> GetMessagesAsync(
        string sessionId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => _catalog.GetMessagesAsync(sessionId, beforeId, afterId, limit, cancellationToken);

    /// <inheritdoc />
    public Task<SessionMessagesResult> GetLaneMessagesAsync(
        string laneId,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => _catalog.GetLaneMessagesAsync(laneId, beforeId, afterId, limit, cancellationToken);

    /// <inheritdoc />
    public async Task<SessionSnapshot> GetSnapshotAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var detail = await _catalog.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (detail.Mode != Mode.Work)
        {
            // RP 每轮从 rounds 重建，没有「开场提示词」可给；不是错误，返回空
            return new SessionSnapshot(string.Empty);
        }

        var session = await _cache.FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new RpcException(
                RpcErrors.Internal,
                "会话状态缺失，无法读取开场提示词",
                new { sessionId, reason = "state_missing" });
        }

        if (!session.TryGetInMemoryChatHistory(out var history) || history is null)
        {
            return new SessionSnapshot(string.Empty);
        }

        var system = history.FirstOrDefault(static message => message.Role == ChatRole.System);
        return new SessionSnapshot(system?.Text ?? string.Empty);
    }

    /// <inheritdoc />
    public Task<SessionExportResult> ExportAsync(
        SessionExportRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.ExportAsync(request, cancellationToken);

    /// <summary>
    /// 工作模式快照：人设文件 + 本次 INDEX 概览，只在建会话时拼一次。
    /// 工具集尚未接入（阶段 4），拼接位置已由 <see cref="PromptAssembler"/> 固定。
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(CancellationToken cancellationToken)
    {
        var content = _persona.Load(Mode.Work);
        var indexDirectory = await BuildIndexDirectoryAsync(cancellationToken).ConfigureAwait(false);
        return PromptAssembler.BuildWorkSystemPrompt(content, indexDirectory);
    }

    private async Task<string?> BuildIndexDirectoryAsync(CancellationToken cancellationToken)
    {
        if (_index is null)
        {
            return null;
        }

        var entries = await _index.ListAsync("work", enabledOnly: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return null;
        }

        return string.Join("\n", entries.Select(static entry =>
        {
            var notes = string.IsNullOrWhiteSpace(entry.Notes) ? string.Empty : $"｜{entry.Notes}";
            return $"- {entry.Title}（{entry.Kind}）｜{entry.Value}{notes}";
        }));
    }
}
