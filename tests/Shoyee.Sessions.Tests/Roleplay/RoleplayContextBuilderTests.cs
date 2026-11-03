using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Sessions.Persona;
using Shoyee.Sessions.Roleplay;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Sessions.Tests.Roleplay;

public sealed class RoleplayContextBuilderTests : IDisposable
{
    private const string Lane = "desktop:roleplay";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-rp-" + Guid.NewGuid().ToString("N"));

    public RoleplayContextBuilderTests() => WorkspaceBootstrap.EnsurePersonaFiles(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_AssemblesZonesInFixedOrder()
    {
        var rounds = new FakeRoundRepository(
            Round(1, Lane, "问一", "答一"),
            Round(2, Lane, "问二", "答二"));
        var summaries = new FakeSummaryRepository(
            new SummaryRecord(1, 30, 30, "全局摘要", DateTimeOffset.UtcNow));
        var builder = CreateBuilder(rounds, summaries);
        var now = new DateTimeOffset(2026, 10, 28, 21, 5, 0, TimeSpan.FromHours(8));

        var context = await builder.BuildAsync(
            Lane, "本轮问题", now, ["<State>心情：平静</State>"]);

        var messages = context.Messages;
        Assert.Equal(12, messages.Count);

        // 1. 背景区（5 条独立 system：守则 / SOUL / WORLD / USER / MEMORY）
        Assert.All(messages.Take(5), m => Assert.Equal(ChatRole.System, m.Role));

        // 2. 摘要区：全局一份
        Assert.Equal(ChatRole.User, messages[5].Role);
        Assert.Contains("全局摘要", messages[5].Text!, StringComparison.Ordinal);

        // 3. 系统注入区
        Assert.Equal(ChatRole.System, messages[6].Role);
        Assert.Contains("心情：平静", messages[6].Text!, StringComparison.Ordinal);

        // 4. 最近完整过程区：同一条 Lane 只在段首标一次渠道
        Assert.EndsWith("[渠道: 桌面]</Channel>问一", messages[7].Text!, StringComparison.Ordinal);
        Assert.Equal("答一", messages[8].Text);
        Assert.Equal("问二", messages[9].Text);
        Assert.Equal("答二", messages[10].Text);

        // 5. 本轮输入：发给模型的带渠道；落库的不带渠道（渠道标记在组装时按切换注入）
        Assert.Same(context.Input, messages[11]);
        Assert.Contains("<Time>", context.Input.Text!, StringComparison.Ordinal);
        Assert.Contains("[渠道: 桌面]", context.Input.Text!, StringComparison.Ordinal);
        Assert.EndsWith("本轮问题", context.Input.Text!, StringComparison.Ordinal);
        Assert.DoesNotContain("<Channel", context.StoredInput.Text!, StringComparison.Ordinal);
        Assert.EndsWith("</Time>本轮问题", context.StoredInput.Text!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_MarksChannelOnlyOnLaneSwitch()
    {
        var rounds = new FakeRoundRepository(
            Round(1, "desktop:roleplay", "问一", "答一"),
            Round(2, "mattermost:roleplay", "问二", "答二"),
            Round(3, "mattermost:roleplay", "问三", "答三"));
        var builder = CreateBuilder(rounds, new FakeSummaryRepository());

        var context = await builder.BuildAsync("desktop:roleplay", "本轮", DateTimeOffset.Now);

        // 背景 5 + 历史 6 + 输入 1
        Assert.Equal(12, context.Messages.Count);
        var history = context.Messages.Skip(5).Take(6).ToList();
        Assert.Contains("[渠道: 桌面]", history[0].Text!, StringComparison.Ordinal);
        Assert.Equal("答一", history[1].Text);
        Assert.Contains("[渠道: 手机]", history[2].Text!, StringComparison.Ordinal);
        Assert.Equal("答二", history[3].Text);
        Assert.Equal("问三", history[4].Text);
        Assert.Equal("答三", history[5].Text);
    }

    [Fact]
    public async Task BuildAsync_SkipsEmptyZones()
    {
        var emptyRoot = Path.Combine(Path.GetTempPath(), "shoyee-rp-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);
        try
        {
            var rounds = new FakeRoundRepository(Round(1, Lane, "问", "答"));
            var builder = new RoleplayContextBuilder(
                new PersonaFiles(emptyRoot), rounds, new FakeSummaryRepository(), new FakeLaneRepository());

            var context = await builder.BuildAsync(Lane, "只有输入", DateTimeOffset.Now);

            Assert.Equal(3, context.Messages.Count);
            Assert.EndsWith("</Channel>问", context.Messages[0].Text!, StringComparison.Ordinal);
            Assert.Equal("答", context.Messages[1].Text);
            Assert.Same(context.Input, context.Messages[2]);
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_StripsEveryInnerVoiceBlockFromHistory()
    {
        var rounds = new FakeRoundRepository(
            Round(1, Lane, "问", "前半<InnerVoice>自检一</InnerVoice>中间<InnerVoice>自检二</InnerVoice>后半"));
        var builder = CreateBuilder(rounds, new FakeSummaryRepository());

        var context = await builder.BuildAsync(Lane, "本轮", DateTimeOffset.Now);

        var answer = Assert.Single(context.Messages, m => m.Role == ChatRole.Assistant);
        Assert.Equal("前半中间后半", answer.Text);
    }

    [Fact]
    public async Task BuildAsync_RequestsOnlyTheRecentWindow()
    {
        var rounds = new FakeRoundRepository();
        var builder = CreateBuilder(rounds, new FakeSummaryRepository());

        await builder.BuildAsync(Lane, "hi", DateTimeOffset.Now);

        Assert.Equal(RoleplayContextBuilder.RecentRoundWindow, rounds.LastRequestedCount);
    }

    [Fact]
    public void BuildInputMessage_ContainsTimeAndRawText()
    {
        var now = new DateTimeOffset(2026, 10, 28, 21, 5, 0, TimeSpan.FromHours(8));

        var message = RoleplayContextBuilder.BuildInputMessage("在吗", now);

        Assert.Equal(ChatRole.User, message.Role);
        Assert.StartsWith("<Time>[消息时间: 2026-10-28", message.Text, StringComparison.Ordinal);
        Assert.EndsWith("</Time>在吗", message.Text, StringComparison.Ordinal);
        Assert.Contains("+08:00", message.Text, StringComparison.Ordinal);
    }

    private RoleplayContextBuilder CreateBuilder(
        IRoundRepository rounds,
        ISummaryRepository summaries)
        => new(new PersonaFiles(_root), rounds, summaries, new FakeLaneRepository());

    private static RoundRecord Round(int roundNo, string laneId, string question, string answer) => new(
        roundNo,
        laneId,
        [new ChatMessage(ChatRole.User, question)],
        [new ChatMessage(ChatRole.Assistant, answer)],
        DateTimeOffset.UtcNow);

    private sealed class FakeLaneRepository : ILaneRepository
    {
        private static readonly LaneRecord[] Lanes =
        [
            new(
                "desktop:roleplay",
                LaneChannel.Desktop,
                Mode.Roleplay,
                "desktop",
                "桌面",
                null,
                null,
                null,
                DateTimeOffset.UnixEpoch),
            new(
                "mattermost:roleplay",
                LaneChannel.Mattermost,
                Mode.Roleplay,
                "mm",
                "手机",
                null,
                null,
                null,
                DateTimeOffset.UnixEpoch),
        ];

        public Task<IReadOnlyList<LaneRecord>> ListAsync(
            Mode? mode = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LaneRecord>>(
                Lanes.Where(l => mode is null || l.Mode == mode).ToList());

        public Task<LaneRecord?> GetAsync(string laneId, CancellationToken cancellationToken = default)
            => Task.FromResult<LaneRecord?>(Lanes.FirstOrDefault(l => l.Id == laneId));

        public Task<LaneRecord> UpsertAsync(LaneRecord lane, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetActiveSessionAsync(
            string laneId,
            string? sessionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TouchInboundAsync(
        string laneId,
        DateTimeOffset at,
        string? target = null,
        CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TouchInteractionAsync(string laneId, DateTimeOffset at, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DateTimeOffset?> GetLastInteractionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeRoundRepository(params RoundRecord[] rounds) : IRoundRepository
    {
        public int? LastRequestedCount { get; private set; }

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
        {
            LastRequestedCount = count;
            return Task.FromResult<IReadOnlyList<RoundRecord>>(rounds);
        }

        public Task<IReadOnlyList<RoundRecord>> GetAfterAsync(
            int afterRoundNo,
            int count = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RoundRecord>>(rounds.Where(r => r.RoundNo > afterRoundNo).ToList());

        public Task<int> GetMaxRoundNoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(rounds.Length == 0 ? 0 : rounds.Max(r => r.RoundNo));

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(rounds.Length);
    }

    private sealed class FakeSummaryRepository(params SummaryRecord[] summaries) : ISummaryRepository
    {
        public Task<SummaryRecord?> GetLatestAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(summaries.LastOrDefault());

        public Task<SummaryRecord> AppendAsync(
            int throughRound,
            int roundCount,
            string content,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
