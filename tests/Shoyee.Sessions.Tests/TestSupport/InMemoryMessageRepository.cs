using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Sessions.Tests.TestSupport;

/// <summary>
/// 内存消息投影仓库：只实现 <c>source_index</c> 游标与去重语义。
/// 投影规则本身在 Storage 里由 <c>ChatMessageProjector</c> 负责，这里只要能区分「会落行 / 不落行」即可。
/// </summary>
internal sealed class InMemoryMessageRepository : IMessageRepository
{
    private readonly List<(MessageRecord Record, int? SourceIndex)> _rows = [];
    private long _nextId = 1;

    /// <summary>当前全部投影行。</summary>
    public IReadOnlyList<MessageRecord> Rows => _rows.Select(static r => r.Record).ToList();

    public Task<MessageRecord?> AppendAsync(
        string laneId,
        string? sessionId,
        int? roundNo,
        ChatMessage message,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("工作模式转录只用 AppendRangeAsync。");

    public Task<IReadOnlyList<MessageRecord>> AppendRangeAsync(
        string laneId,
        string sessionId,
        IReadOnlyList<ChatMessage> messages,
        int startIndex,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        var added = new List<MessageRecord>();
        for (var i = 0; i < messages.Count; i++)
        {
            var sourceIndex = startIndex + i;
            var projection = Project(messages[i]);
            if (projection is null)
            {
                continue;
            }

            if (_rows.Any(r => r.Record.SessionId == sessionId && r.SourceIndex == sourceIndex))
            {
                continue;
            }

            var record = new MessageRecord(
                _nextId++,
                laneId,
                sessionId,
                null,
                messages[i].Role.Value,
                projection.Value.Kind,
                projection.Value.Content,
                projection.Value.ToolName,
                at ?? DateTimeOffset.UtcNow);
            _rows.Add((record, sourceIndex));
            added.Add(record);
        }

        return Task.FromResult<IReadOnlyList<MessageRecord>>(added);
    }

    public Task<int> GetProjectedIndexAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var max = _rows
            .Where(r => r.Record.SessionId == sessionId && r.SourceIndex is not null)
            .Select(r => r.SourceIndex!.Value)
            .DefaultIfEmpty(-1)
            .Max();
        return Task.FromResult(max + 1);
    }

    public Task<int> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var removed = _rows.RemoveAll(r => r.Record.SessionId == sessionId);
        return Task.FromResult(removed);
    }

    public Task<MessageRecord?> GetLastAssistantAsync(
        string? laneId = null,
        string? sessionId = null,
        int? roundNo = null,
        CancellationToken cancellationToken = default)
    {
        var row = _rows
            .Select(static r => r.Record)
            .Where(r => r.Role == "assistant"
                        && r.Content.Length > 0
                        && r.Kind is MessageKind.Text or MessageKind.Spoken)
            .Where(r => laneId is null || r.LaneId == laneId)
            .Where(r => sessionId is null || r.SessionId == sessionId)
            .Where(r => roundNo is null || r.RoundNo == roundNo)
            .OrderByDescending(r => r.Id)
            .FirstOrDefault();
        return Task.FromResult(row);
    }

    public Task<MessageRecord?> GetAsync(long id, CancellationToken cancellationToken = default)
        => Task.FromResult<MessageRecord?>(_rows.FirstOrDefault(row => row.Record.Id == id).Record);

    public Task<MessageRecord?> AttachToToolResultAsync(
        string laneId,
        string resultMarker,
        IReadOnlyList<MessageAttachment> attachments,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("消息投影回写只在 SQLite 仓库和 Daemon 投递里覆盖。");

    public Task<IReadOnlyList<MessageRecord>> GetPageAsync(
        string? laneId = null,
        string? sessionId = null,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MessageRecord>>(Rows
            .Where(r => laneId is null || r.LaneId == laneId)
            .Where(r => sessionId is null || r.SessionId == sessionId)
            .OrderBy(r => r.Id)
            .Take(limit)
            .ToList());

    public Task<int> CountAsync(string? laneId = null, string? sessionId = null, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.Count(r =>
            (laneId is null || r.Record.LaneId == laneId)
            && (sessionId is null || r.Record.SessionId == sessionId)));

    /// <summary>与真实投影规则对齐到「有没有可见行」这一层：纯推理/纯用量不落行。</summary>
    private static (MessageKind Kind, string Content, string? ToolName)? Project(ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue("_is_summary", out var summary) == true && summary is true)
        {
            return (MessageKind.Summary, message.Text ?? string.Empty, null);
        }

        if (message.Contents.OfType<FunctionResultContent>().Any())
        {
            return (MessageKind.ToolResult, string.Empty, null);
        }

        if (message.Contents.OfType<FunctionCallContent>().FirstOrDefault() is { } call)
        {
            return (MessageKind.ToolCall, message.Text ?? string.Empty, call.Name);
        }

        if (message.Contents.OfType<TextContent>().Any())
        {
            return (MessageKind.Text, message.Text ?? string.Empty, null);
        }

        return null;
    }
}
