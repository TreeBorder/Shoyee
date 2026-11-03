using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>
/// 只记录投影的假消息仓库：工作模式按游标追加，RP 单条追加；投影规则简化到「有可见正文/工具结果就落行」。
/// 真实的 <c>ChatMessageProjector</c> 规则在 Storage 的测试里覆盖。
/// </summary>
internal sealed class RecordingMessageRepository : IMessageRepository
{
    private readonly List<MessageRecord> _rows = [];
    private readonly Dictionary<long, int> _sourceIndexes = [];
    private long _nextId = 1;

    public IReadOnlyList<MessageRecord> Rows => _rows;

    public Task<MessageRecord?> AppendAsync(
        string laneId,
        string? sessionId,
        int? roundNo,
        ChatMessage message,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CreateRow(laneId, sessionId, roundNo, message, at, sourceIndex: null));

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
            var row = CreateRow(laneId, sessionId, roundNo: null, messages[i], at, sourceIndex: startIndex + i);
            if (row is not null)
            {
                added.Add(row);
            }
        }

        return Task.FromResult<IReadOnlyList<MessageRecord>>(added);
    }

    public Task<int> GetProjectedIndexAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var max = _rows
            .Where(r => r.SessionId == sessionId && _sourceIndexes.ContainsKey(r.Id))
            .Select(r => _sourceIndexes[r.Id])
            .DefaultIfEmpty(-1)
            .Max();
        return Task.FromResult(max + 1);
    }

    public Task<int> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.RemoveAll(r => r.SessionId == sessionId));

    public Task<MessageRecord?> GetLastAssistantAsync(
        string? laneId = null,
        string? sessionId = null,
        int? roundNo = null,
        CancellationToken cancellationToken = default)
    {
        var row = _rows
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

    public Task<MessageRecord?> AttachToToolResultAsync(
        string laneId,
        string resultMarker,
        IReadOnlyList<MessageAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        var index = _rows.FindLastIndex(r => r.LaneId == laneId
                                              && r.Kind == MessageKind.ToolResult
                                              && r.ToolResult?.Contains(resultMarker, StringComparison.Ordinal) == true);
        if (index < 0)
        {
            return Task.FromResult<MessageRecord?>(null);
        }

        var row = _rows[index];
        var merged = (row.Attachments ?? [])
            .Concat(attachments)
            .DistinctBy(static a => a.Path)
            .ToList();
        var updated = row with { Attachments = merged };
        _rows[index] = updated;
        return Task.FromResult<MessageRecord?>(updated);
    }

    public Task<MessageRecord?> GetAsync(long id, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.FirstOrDefault(row => row.Id == id));

    public Task<IReadOnlyList<MessageRecord>> GetPageAsync(
        string? laneId = null,
        string? sessionId = null,
        long? beforeId = null,
        long? afterId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _rows
            .Where(r => laneId is null || r.LaneId == laneId)
            .Where(r => sessionId is null || r.SessionId == sessionId);

        List<MessageRecord> page;
        if (afterId is { } after)
        {
            page = query.Where(r => r.Id > after).OrderBy(r => r.Id).Take(limit).ToList();
        }
        else if (beforeId is { } before)
        {
            page = query.Where(r => r.Id < before).OrderByDescending(r => r.Id).Take(limit).ToList();
            page.Reverse();
        }
        else
        {
            page = query.OrderByDescending(r => r.Id).Take(limit).ToList();
            page.Reverse();
        }

        return Task.FromResult<IReadOnlyList<MessageRecord>>(page);
    }

    public Task<int> CountAsync(string? laneId = null, string? sessionId = null, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.Count(r =>
            (laneId is null || r.LaneId == laneId)
            && (sessionId is null || r.SessionId == sessionId)));

    private MessageRecord? CreateRow(
        string laneId,
        string? sessionId,
        int? roundNo,
        ChatMessage message,
        DateTimeOffset? at,
        int? sourceIndex)
    {
        var props = message.AdditionalProperties;
        if (props?.TryGetValue(ChatMessageMarkers.SystemInput, out var systemInput) == true && systemInput is true)
        {
            return null; // 系统注入不进可见历史
        }

        var isToolResult = message.Contents.OfType<FunctionResultContent>().Any();
        var text = props?.TryGetValue(ChatMessageMarkers.DisplayText, out var display) == true && display is string displayText
            ? displayText
            : message.Text;
        if (string.IsNullOrEmpty(text) && !isToolResult)
        {
            return null;
        }

        var attachments = props?.TryGetValue(ChatMessageMarkers.Attachments, out var value) == true
                          && value is IReadOnlyList<MessageAttachment> list
            ? list
            : null;

        var id = _nextId++;
        var row = new MessageRecord(
            id,
            laneId,
            sessionId,
            roundNo,
            message.Role.Value,
            isToolResult ? MessageKind.ToolResult : MessageKind.Text,
            text ?? string.Empty,
            null,
            at ?? DateTimeOffset.UtcNow,
            attachments);
        _rows.Add(row);
        if (sourceIndex is not null)
        {
            _sourceIndexes[id] = sourceIndex.Value;
        }

        return row;
    }
}
