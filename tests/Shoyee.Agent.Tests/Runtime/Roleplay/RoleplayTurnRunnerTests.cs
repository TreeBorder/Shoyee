using Microsoft.Extensions.AI;
using Shoyee.Agent.Composition;
using Shoyee.Agent.Runtime.Roleplay;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Contracts.Domain;
using Shoyee.Sessions.Persona;
using Shoyee.Sessions.Roleplay;
using Shoyee.Sessions.Summary;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Agent.Tests.Runtime.Roleplay;

public sealed class RoleplayTurnRunnerTests : IDisposable
{
    private const string Lane = "desktop:roleplay";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-rp-runner-" + Guid.NewGuid().ToString("N"));

    public RoleplayTurnRunnerTests() => WorkspaceBootstrap.EnsurePersonaFiles(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SendsAssembledContext_AndPersistsRound()
    {
        var rounds = new FakeRoundRepository();
        var summaries = new FakeSummaryRepository();
        var runner = CreateRunner(rounds, summaries);

        var result = await runner.RunAsync(Lane, "在吗", new DateTimeOffset(2026, 10, 28, 21, 5, 0, TimeSpan.FromHours(8)));

        Assert.NotNull(result);
        Assert.Equal(1, result!.Round.RoundNo);
        Assert.Equal("应答", result.Response.Single().Text);
        Assert.Single(rounds.Appended);

        var sent = rounds.Client.Calls[0];
        Assert.Contains("角色扮演守则", sent[0].Text!, StringComparison.Ordinal);
        Assert.Contains("在吗", sent[^1].Text!, StringComparison.Ordinal);
        // 落库的输入不带渠道标记（下轮组装时按 Lane 切换重新注入）
        Assert.DoesNotContain("<Channel", rounds.Appended[0].Request[0].Text!, StringComparison.Ordinal);
        Assert.Empty(summaries.Appended);
    }

    [Fact]
    public async Task RunAsync_AtThirtiethRound_TriggersSummary()
    {
        var rounds = new FakeRoundRepository(Enumerable.Range(1, 29));
        var summaries = new FakeSummaryRepository();
        var runner = CreateRunner(rounds, summaries);
        var summarizeCalls = 0;

        var result = await runner.RunAsync(
            Lane,
            "第三十轮",
            DateTimeOffset.Now,
            summarize: (_, _) =>
            {
                summarizeCalls++;
                return Task.FromResult("累计摘要");
            });

        Assert.NotNull(result);
        Assert.Equal(30, result!.Round.RoundNo);
        Assert.Equal(1, summarizeCalls);
        var summary = Assert.Single(summaries.Appended);
        Assert.Equal(30, summary.ThroughRound);
        Assert.StartsWith("累计摘要", summary.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_SystemInput_IsMarkedForProjection()
    {
        var rounds = new FakeRoundRepository();
        var runner = CreateRunner(rounds, new FakeSummaryRepository());

        await runner.RunAsync(Lane, "[系统消息]现在是心跳时刻[/]", DateTimeOffset.Now, systemInput: true);
        await runner.RunAsync(Lane, "正常聊天", DateTimeOffset.Now);

        var injected = rounds.Appended[0].Request[0];
        Assert.True(injected.AdditionalProperties?[ChatMessageMarkers.SystemInput] is true);
        // 注入内容照样进模型上下文
        Assert.Contains("现在是心跳时刻", rounds.Client.Calls[0][^1].Text!, StringComparison.Ordinal);

        var normal = rounds.Appended[1].Request[0];
        Assert.False(normal.AdditionalProperties?.TryGetValue(ChatMessageMarkers.SystemInput, out _) == true);
    }

    private RoleplayTurnRunner CreateRunner(FakeRoundRepository rounds, FakeSummaryRepository summaries)
    {
        var client = rounds.Client;
        var recorder = new RoundRecorder(rounds);
        var agent = new RoleplayAgentFactory(client).CreateAgent(recorder);
        var builder = new RoleplayContextBuilder(
            new PersonaFiles(_root), rounds, summaries, new InMemoryLaneRepository());
        var summarizer = new RoleplaySummarizer(new PersonaFiles(_root), rounds, summaries);
        return new RoleplayTurnRunner(agent, builder, recorder, summarizer);
    }

    private sealed class FakeRoundRepository : IRoundRepository
    {
        private readonly List<RoundRecord> _rounds;

        public FakeRoundRepository(IEnumerable<int>? seed = null)
        {
            _rounds = (seed ?? []).Select(i => new RoundRecord(
                i,
                Lane,
                [new ChatMessage(ChatRole.User, $"历史-{i}")],
                [new ChatMessage(ChatRole.Assistant, $"回答-{i}")],
                DateTimeOffset.UtcNow.AddMinutes(-i))).ToList();
        }

        public RecordingChatClient Client { get; } = new();

        public List<RoundRecord> Appended { get; } = [];

        public Task<RoundRecord> AppendAsync(
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
            return Task.FromResult(record);
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

    private sealed class FakeSummaryRepository : ISummaryRepository
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
}
