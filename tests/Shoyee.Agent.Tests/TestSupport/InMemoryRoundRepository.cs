using Microsoft.Extensions.AI;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>内存版 RP 轮次仓库；顺带暴露 <see cref="Client"/> 供断言发给模型的消息。</summary>
internal sealed class InMemoryRoundRepository : IRoundRepository
{
    private const string DefaultLane = "desktop:roleplay";

    private readonly List<RoundRecord> _rounds;
    private readonly IMessageRepository? _messages;

    public InMemoryRoundRepository(IEnumerable<int>? seed = null, IMessageRepository? messages = null)
    {
        _rounds = (seed ?? []).Select(i => new RoundRecord(
            i,
            DefaultLane,
            [new ChatMessage(ChatRole.User, $"历史-{i}")],
            [new ChatMessage(ChatRole.Assistant, $"回答-{i}")],
            DateTimeOffset.UtcNow.AddMinutes(-i))).ToList();
        _messages = messages;
    }

    public RecordingChatClient Client { get; } = new();

    public List<RoundRecord> Appended { get; } = [];

    public async Task<RoundRecord> AppendAsync(
        string laneId,
        IReadOnlyList<ChatMessage> request,
        IReadOnlyList<ChatMessage> response,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        var roundNo = _rounds.Count == 0 ? 1 : _rounds.Max(r => r.RoundNo) + 1;
        var record = new RoundRecord(roundNo, laneId, request, response, at ?? DateTimeOffset.UtcNow);
        _rounds.Add(record);
        Appended.Add(record);

        // 与真实 RoundRepository 一致：同一轮顺带写消息投影（测试里也要能取到 messageId / lane.messages）
        if (_messages is not null)
        {
            foreach (var message in request.Concat(response))
            {
                await _messages.AppendAsync(laneId, null, roundNo, message, at, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return record;
    }

    public Task<IReadOnlyList<RoundRecord>> GetRecentAsync(
        int count = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RoundRecord>>(
            _rounds.Count <= count ? _rounds.ToList() : _rounds.Skip(_rounds.Count - count).ToList());

    public Task<IReadOnlyList<RoundRecord>> GetAfterAsync(
        int afterRoundNo,
        int count = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RoundRecord>>(
            _rounds.Where(r => r.RoundNo > afterRoundNo).Take(count).ToList());

    public Task<int> GetMaxRoundNoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_rounds.Count == 0 ? 0 : _rounds.Max(r => r.RoundNo));

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_rounds.Count);
}
