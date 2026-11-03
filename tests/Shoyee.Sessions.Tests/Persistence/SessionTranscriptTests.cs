using Microsoft.Extensions.AI;
using Shoyee.Sessions.Persistence;
using Shoyee.Sessions.Tests.TestSupport;
using Xunit;

namespace Shoyee.Sessions.Tests.Persistence;

/// <summary>工作模式转录投影：增量、幂等、真源变短时重建。</summary>
public sealed class SessionTranscriptTests
{
    private const string Lane = "mattermost:work";
    private const string Session = "ses_1";

    [Fact]
    public async Task 首次同步_抄下全部可落行消息()
    {
        var repository = new InMemoryMessageRepository();
        var transcript = new SessionTranscript(repository);
        var history = new ChatMessage[]
        {
            new(ChatRole.System, "工作人设"),
            new(ChatRole.User, "第一句"),
            new(ChatRole.Assistant, "应答"),
        };

        var added = await transcript.SyncAsync(Lane, Session, history);

        Assert.Equal(3, added.Count);
        Assert.Equal(["工作人设", "第一句", "应答"], repository.Rows.Select(r => r.Content));
        Assert.Equal(3, await repository.GetProjectedIndexAsync(Session));
        Assert.All(repository.Rows, r => Assert.Equal(Lane, r.LaneId));
    }

    [Fact]
    public async Task 第二次同步_只抄新增部分()
    {
        var repository = new InMemoryMessageRepository();
        var transcript = new SessionTranscript(repository);
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "工作人设"),
            new(ChatRole.User, "第一句"),
            new(ChatRole.Assistant, "应答"),
        };
        await transcript.SyncAsync(Lane, Session, history);

        history.Add(new ChatMessage(ChatRole.User, "第二句"));
        history.Add(new ChatMessage(ChatRole.Assistant, "再应答"));
        var added = await transcript.SyncAsync(Lane, Session, history);

        Assert.Equal(2, added.Count);
        Assert.Equal(
            ["工作人设", "第一句", "应答", "第二句", "再应答"],
            repository.Rows.Select(r => r.Content));
        Assert.Equal(5, await repository.GetProjectedIndexAsync(Session));
    }

    [Fact]
    public async Task 历史没变_不重复抄()
    {
        var repository = new InMemoryMessageRepository();
        var transcript = new SessionTranscript(repository);
        var history = new ChatMessage[]
        {
            new(ChatRole.User, "只有一句"),
        };
        await transcript.SyncAsync(Lane, Session, history);

        var added = await transcript.SyncAsync(Lane, Session, history);

        Assert.Empty(added);
        Assert.Single(repository.Rows);
    }

    [Fact]
    public async Task 真源变短_自动整段重建而不是继续错位()
    {
        var repository = new InMemoryMessageRepository();
        var transcript = new SessionTranscript(repository);
        await transcript.SyncAsync(Lane, Session,
        [
            new ChatMessage(ChatRole.System, "旧人设"),
            new ChatMessage(ChatRole.User, "旧一"),
            new ChatMessage(ChatRole.User, "旧二"),
            new ChatMessage(ChatRole.Assistant, "旧三"),
        ]);

        var added = await transcript.SyncAsync(Lane, Session,
        [
            new ChatMessage(ChatRole.System, "压缩后摘要"),
            new ChatMessage(ChatRole.User, "压缩后的问题"),
        ]);

        Assert.Equal(2, added.Count);
        Assert.Equal(["压缩后摘要", "压缩后的问题"], repository.Rows.Select(r => r.Content));
        Assert.Equal(2, await repository.GetProjectedIndexAsync(Session));
    }

    [Fact]
    public async Task 重建_先删旧投影再全量重抄()
    {
        var repository = new InMemoryMessageRepository();
        var transcript = new SessionTranscript(repository);
        await transcript.SyncAsync(Lane, Session,
        [
            new ChatMessage(ChatRole.User, "甲"),
            new ChatMessage(ChatRole.Assistant, "乙"),
        ]);

        var rebuilt = await transcript.RebuildAsync(Lane, Session,
        [
            new ChatMessage(ChatRole.User, "丙"),
        ]);

        Assert.Single(rebuilt);
        Assert.Equal(["丙"], repository.Rows.Select(r => r.Content));
        Assert.Equal(1, await repository.GetProjectedIndexAsync(Session));
    }

    [Fact]
    public async Task 只有推理内容的消息不落行_也不影响后续下标()
    {
        var repository = new InMemoryMessageRepository();
        var transcript = new SessionTranscript(repository);
        var history = new ChatMessage[]
        {
            new(ChatRole.User, "问题"),
            new(ChatRole.Assistant, [new TextReasoningContent("只想了没说话")]),
            new(ChatRole.Assistant, "回答"),
        };

        var added = await transcript.SyncAsync(Lane, Session, history);

        Assert.Equal(2, added.Count);
        Assert.Equal(["问题", "回答"], repository.Rows.Select(r => r.Content));
        Assert.Equal(3, await repository.GetProjectedIndexAsync(Session));
    }
}
