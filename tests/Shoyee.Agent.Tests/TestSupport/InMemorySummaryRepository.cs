using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>内存版 RP 摘要仓库（全局一份累积摘要）。</summary>
internal sealed class InMemorySummaryRepository : ISummaryRepository
{
    public List<SummaryRecord> Appended { get; } = [];

    public Task<SummaryRecord?> GetLatestAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Appended.LastOrDefault());

    public Task<SummaryRecord> AppendAsync(
        int throughRound,
        int roundCount,
        string content,
        CancellationToken cancellationToken = default)
    {
        var record = new SummaryRecord(
            Appended.Count + 1, throughRound, roundCount, content, DateTimeOffset.UtcNow);
        Appended.Add(record);
        return Task.FromResult(record);
    }
}
