using Microsoft.Extensions.AI;
using Shoyee.Sessions.Persona;
using Shoyee.Sessions.Summary;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Sessions.Tests.Summary;

public sealed class RoleplaySummarizerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-summary-" + Guid.NewGuid().ToString("N"));

    public RoleplaySummarizerTests() => WorkspaceBootstrap.EnsurePersonaFiles(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public void ShouldSummarize_OnlyOnMultiplesOf30(int maxRoundNo, bool expected)
        => Assert.Equal(expected, RoleplaySummarizer.ShouldSummarize(maxRoundNo));

    [Fact]
    public async Task TryBuildRequestAsync_NotDue_ReturnsNull()
    {
        var summarizer = CreateSummarizer(rounds: MakeRounds(1, 29), latest: null);

        Assert.Null(await summarizer.TryBuildRequestAsync());
    }

    [Fact]
    public async Task TryBuildRequestAsync_AlreadyCovered_ReturnsNull()
    {
        var latest = new SummaryRecord(1, 30, 30, "旧摘要", DateTimeOffset.UtcNow);
        var summarizer = CreateSummarizer(rounds: MakeRounds(1, 30), latest: latest);

        Assert.Null(await summarizer.TryBuildRequestAsync());
    }

    [Fact]
    public async Task TryBuildRequestAsync_BuildsCumulativePrompt()
    {
        var latest = new SummaryRecord(1, 30, 30, "旧摘要", DateTimeOffset.UtcNow);
        var summarizer = CreateSummarizer(rounds: MakeRounds(1, 60), latest: latest);

        var request = await summarizer.TryBuildRequestAsync();

        Assert.NotNull(request);
        Assert.Equal(60, request!.ThroughRound);
        Assert.Equal(30, request.RoundCount);
        Assert.Contains("## SOUL", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("## 历史摘要", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("旧摘要", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("## 冗余历史消息", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("新-30", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("## 还未摘要的消息", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("新-60", request.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrySummarizeAsync_AppendsCumulativeSummaryWithTimestamp()
    {
        var summaries = new FakeSummaryRepository(null);
        var summarizer = CreateSummarizer(MakeRounds(1, 60), null, summaries);
        var now = new DateTimeOffset(2026, 10, 28, 21, 5, 0, TimeSpan.FromHours(8));

        var record = await summarizer.TrySummarizeAsync((_, _) => Task.FromResult("累计摘要"), now);

        Assert.NotNull(record);
        Assert.Equal(60, record!.ThroughRound);
        Assert.Equal(30, record.RoundCount);
        Assert.StartsWith("累计摘要", record.Content, StringComparison.Ordinal);
        Assert.Contains("[SummaryUpdated]2026-10-28 21:05 +08:00[/SummaryUpdated]", record.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrySummarizeAsync_EmptyText_DoesNotAppend()
    {
        var summaries = new FakeSummaryRepository(null);
        var summarizer = CreateSummarizer(MakeRounds(1, 60), null, summaries);

        var record = await summarizer.TrySummarizeAsync((_, _) => Task.FromResult("   "));

        Assert.Null(record);
        Assert.Empty(summaries.Appended);
    }

    [Fact]
    public async Task TrySummarizeAsync_NotDue_DoesNotCallModel()
    {
        var called = false;
        var summarizer = CreateSummarizer(MakeRounds(1, 29), null);

        var record = await summarizer.TrySummarizeAsync((_, _) =>
        {
            called = true;
            return Task.FromResult("不该调用");
        });

        Assert.Null(record);
        Assert.False(called);
    }

    private RoleplaySummarizer CreateSummarizer(
        IReadOnlyList<RoundRecord> rounds,
        SummaryRecord? latest,
        FakeSummaryRepository? summaries = null)
        => new(new PersonaFiles(_root), new FakeRoundRepository(rounds), summaries ?? new FakeSummaryRepository(latest));

    private static IReadOnlyList<RoundRecord> MakeRounds(int from, int to)
        => Enumerable.Range(from, to - from + 1)
            .Select(i => new RoundRecord(
                i,
                "desktop:roleplay",
                [new ChatMessage(ChatRole.User, $"新-{i}")],
                [new ChatMessage(ChatRole.Assistant, $"答-{i}")],
                DateTimeOffset.UtcNow.AddMinutes(i)))
            .ToList();

    private sealed class FakeRoundRepository(IReadOnlyList<RoundRecord> rounds) : IRoundRepository
    {
        public Task<RoundRecord> AppendAsync(
            string laneId,
            IReadOnlyList<ChatMessage> request,
            IReadOnlyList<ChatMessage> response,
            DateTimeOffset? at = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RoundRecord>> GetRecentAsync(
            int count = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RoundRecord>>(
                rounds.Count <= count ? rounds : rounds.Skip(rounds.Count - count).ToList());

        public Task<IReadOnlyList<RoundRecord>> GetAfterAsync(
            int afterRoundNo,
            int count = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RoundRecord>>(
                rounds.Where(r => r.RoundNo > afterRoundNo).Take(count).ToList());

        public Task<int> GetMaxRoundNoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(rounds.Count == 0 ? 0 : rounds.Max(r => r.RoundNo));

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(rounds.Count);
    }

    private sealed class FakeSummaryRepository(SummaryRecord? latest) : ISummaryRepository
    {
        public List<SummaryRecord> Appended { get; } = [];

        public Task<SummaryRecord?> GetLatestAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(latest);

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
}
