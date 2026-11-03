using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Sessions.Roleplay;
using Shoyee.Sessions.Turns;
using Shoyee.Sessions.Tests.TestSupport;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Sessions.Tests.Roleplay;

public sealed class RoundRecorderTests
{
    private const string Lane = "desktop:roleplay";

    [Fact]
    public async Task InvokedAsync_AccumulatesResponseMessages_UntilTaken()
    {
        var recorder = new RoundRecorder(new CapturingRoundRepository());
        var agent = StubChatClient.CreateAgent();
        var session = await agent.CreateSessionAsync();

        await recorder.InvokedAsync(new AIContextProvider.InvokedContext(agent, session, [], [Msg("第一段")]));
        await recorder.InvokedAsync(new AIContextProvider.InvokedContext(agent, session, [], [Msg("第二段")]));

        var taken = recorder.TakeResponse(session);

        Assert.Equal(["第一段", "第二段"], taken.Select(m => m.Text));
        Assert.Empty(recorder.TakeResponse(session));
    }

    [Fact]
    public async Task CompleteRoundAsync_RepairsDanglingToolCall()
    {
        var repository = new CapturingRoundRepository();
        var recorder = new RoundRecorder(repository);
        var agent = StubChatClient.CreateAgent();
        var session = await agent.CreateSessionAsync();
        var call = new FunctionCallContent("call_1", "shell", new Dictionary<string, object?> { ["command"] = "ls" });

        await recorder.InvokedAsync(new AIContextProvider.InvokedContext(agent, session, [], [new ChatMessage(ChatRole.Assistant, [call])]));
        var round = await recorder.CompleteRoundAsync(Lane, session, Msg("跑一下"));

        Assert.NotNull(round);
        Assert.Equal(2, round!.Response.Count);
        var tool = round.Response[1];
        Assert.Equal(ChatRole.Tool, tool.Role);
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(tool.Contents));
        Assert.Equal("call_1", result.CallId);
        Assert.Equal(ToolCallRepair.InterruptedResultText, result.Result);
    }

    [Fact]
    public async Task CompleteRoundAsync_KeepsToolCallThatAlreadyHasResult()
    {
        var recorder = new RoundRecorder(new CapturingRoundRepository());
        var agent = StubChatClient.CreateAgent();
        var session = await agent.CreateSessionAsync();
        var call = new FunctionCallContent("call_1", "shell");
        var result = new FunctionResultContent("call_1", "done");

        await recorder.InvokedAsync(new AIContextProvider.InvokedContext(agent, session, [],
        [
            new ChatMessage(ChatRole.Assistant, [call]),
            new ChatMessage(ChatRole.Tool, [result]),
        ]));
        var round = await recorder.CompleteRoundAsync(Lane, session, Msg("跑一下"));

        Assert.NotNull(round);
        Assert.Equal(2, round!.Response.Count);
        Assert.DoesNotContain(
            round.Response.Skip(2),
            m => m.Contents.OfType<FunctionResultContent>().Any());
    }

    [Fact]
    public async Task CompleteRoundAsync_WithoutResponse_DoesNotWrite()
    {
        var repository = new CapturingRoundRepository();
        var recorder = new RoundRecorder(repository);
        var agent = StubChatClient.CreateAgent();
        var session = await agent.CreateSessionAsync();

        var round = await recorder.CompleteRoundAsync(Lane, session, Msg("空跑"));

        Assert.Null(round);
        Assert.Empty(repository.Appended);
    }

    private static ChatMessage Msg(string text) => new(ChatRole.Assistant, text);

    private sealed class CapturingRoundRepository : IRoundRepository
    {
        public List<(string LaneId, IReadOnlyList<ChatMessage> Request, IReadOnlyList<ChatMessage> Response)> Appended { get; } = [];

        public Task<RoundRecord> AppendAsync(
            string laneId,
            IReadOnlyList<ChatMessage> request,
            IReadOnlyList<ChatMessage> response,
            DateTimeOffset? at = null,
            CancellationToken cancellationToken = default)
        {
            Appended.Add((laneId, request, response));
            return Task.FromResult(new RoundRecord(
                Appended.Count, laneId, request, response, at ?? DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<RoundRecord>> GetRecentAsync(
            int count = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RoundRecord>>([]);

        public Task<IReadOnlyList<RoundRecord>> GetAfterAsync(
            int afterRoundNo,
            int count = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RoundRecord>>([]);

        public Task<int> GetMaxRoundNoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Appended.Count);

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Appended.Count);
    }
}
