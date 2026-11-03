using Shoyee.Channels.Mattermost;
using Xunit;

namespace Shoyee.Channels.Mattermost.Tests;

/// <summary>Mattermost 协议层：双重编码解析、门控、thread 寻址、长文切分。</summary>
public sealed class MattermostProtocolTests
{
    [Fact]
    public void ParsePostedEvent_ExtractsPostAndMention()
    {
        var post = MattermostProtocol.ParsePostedEvent(EventJson(), "shoyee", "bot_1");

        Assert.NotNull(post);
        Assert.Equal("p1", post.Id);
        Assert.Equal("u1", post.UserId);
        Assert.Equal("c1", post.ChannelId);
        Assert.Equal("@shoyee 你好", post.Message);
        Assert.Equal("O", post.ChannelType);
        Assert.True(post.Mentioned);
        Assert.Equal(["f1"], post.FileIds);
    }

    [Fact]
    public void ParsePostedEvent_IgnoresOtherEvents()
    {
        Assert.Null(MattermostProtocol.ParsePostedEvent("""{"event":"typing"}""", "shoyee", "bot_1"));
    }

    [Fact]
    public void ParsePostedEvent_FallsBackToMetadataFiles()
    {
        var json = """
            {"event":"posted","data":{"channel_type":"D","post":"{\"id\":\"p1\",\"user_id\":\"u1\",\"channel_id\":\"c1\",\"message\":\"hi\",\"metadata\":{\"files\":[{\"id\":\"f9\"}]}}"}}
            """;

        var post = MattermostProtocol.ParsePostedEvent(json, "shoyee", "bot_1");

        Assert.NotNull(post);
        Assert.Equal(["f9"], post.FileIds);
    }

    [Fact]
    public void ShouldHandle_DmNeedsNoMention()
    {
        var post = Post(channelType: "D", mentioned: false);

        Assert.True(MattermostProtocol.ShouldHandle(post, "bot_1", null, requireMention: true));
    }

    [Fact]
    public void ShouldHandle_ChannelRequiresMention()
    {
        Assert.False(MattermostProtocol.ShouldHandle(Post("O", mentioned: false), "bot_1", null, true));
        Assert.True(MattermostProtocol.ShouldHandle(Post("O", mentioned: true), "bot_1", null, true));
        Assert.True(MattermostProtocol.ShouldHandle(Post("O", mentioned: false), "bot_1", null, requireMention: false));
    }

    [Fact]
    public void ShouldHandle_FiltersSelfSystemAndNonAllowedUsers()
    {
        Assert.False(MattermostProtocol.ShouldHandle(Post("D", userId: "bot_1"), "bot_1", null, false));
        Assert.False(MattermostProtocol.ShouldHandle(Post("D", type: "system_join_channel"), "bot_1", null, false));
        Assert.False(MattermostProtocol.ShouldHandle(Post("D"), "bot_1", ["someone_else"], false));
    }

    [Fact]
    public void BuildTarget_TopLevelPostIsItsOwnThread()
    {
        Assert.Equal("mattermost:channel:c1:p1", MattermostProtocol.BuildTarget(Post("O")));
    }

    [Fact]
    public void BuildTarget_ReplyUsesRoot()
    {
        Assert.Equal("mattermost:channel:c1:root", MattermostProtocol.BuildTarget(Post("O", rootId: "root")));
    }

    [Fact]
    public void BuildTarget_DmHasNoThread()
    {
        Assert.Equal("mattermost:dm:c1", MattermostProtocol.BuildTarget(Post("D")));
    }

    [Fact]
    public void ParseTarget_RoundTrips()
    {
        Assert.Equal(("c1", "root"), MattermostProtocol.ParseTarget("mattermost:channel:c1:root"));
        Assert.Equal(("c1", (string?)null), MattermostProtocol.ParseTarget("mattermost:dm:c1"));
        Assert.Throws<FormatException>(() => MattermostProtocol.ParseTarget("c1"));
    }

    [Theory]
    [InlineData("@shoyee 你好", "你好")]
    [InlineData("  @Shoyee   在吗", "在吗")]
    [InlineData("没有提及", "没有提及")]
    public void StripMention_RemovesLeadingBotMention(string input, string expected)
        => Assert.Equal(expected, MattermostProtocol.StripMention(input, "shoyee"));

    [Fact]
    public void SplitPost_KeepsEverythingAndRespectsLimit()
    {
        var text = string.Join("\n", Enumerable.Repeat("这是一行有点长的中文内容。", 400));

        var chunks = MattermostProtocol.SplitPost(text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= MattermostProtocol.MaxPostLength));
        Assert.Equal(text.Replace("\n", string.Empty), string.Concat(chunks).Replace("\n", string.Empty));
    }

    [Fact]
    public void SplitPost_ShortTextIsSingleChunk()
        => Assert.Equal(["hi"], MattermostProtocol.SplitPost("hi"));

    private static string EventJson() => """
        {
          "event": "posted",
          "data": {
            "channel_type": "O",
            "post": "{\"id\":\"p1\",\"user_id\":\"u1\",\"channel_id\":\"c1\",\"message\":\"@shoyee 你好\",\"root_id\":\"\",\"type\":\"\",\"file_ids\":[\"f1\"]}"
          }
        }
        """;

    private static MattermostPost Post(
        string channelType,
        bool mentioned = true,
        string rootId = "",
        string userId = "u1",
        string type = "") => new(
        "p1",
        userId,
        "c1",
        "hi",
        rootId,
        type,
        channelType,
        mentioned,
        []);
}
