using System.Text.Json;
using Shoyee.Channels.WeChat;
using Xunit;

namespace Shoyee.Channels.WeChat.Tests;

/// <summary>微信协议层：消息解析、回投地址、长文切分、内容指纹。</summary>
public sealed class WeChatProtocolTests
{
    [Fact]
    public void ParseMessage_TextItem()
    {
        var message = WeChatProtocol.ParseMessage(Message());

        Assert.NotNull(message);
        Assert.Equal("m1", message.MessageId);
        Assert.Equal("u1", message.FromUserId);
        Assert.Equal("ctx", message.ContextToken);
        Assert.Equal("你好", message.Text);
        Assert.Null(message.GroupId);
        Assert.Empty(message.Media ?? []);
    }

    [Fact]
    public void ParseMessage_ReferenceIsFlattenedAndGroupKept()
    {
        var json = """
            {"message_id":"m2","from_user_id":"u1","group_id":"g1","item_list":[
              {"type":1,"text_item":{"text":"同意"},"ref_msg":{"title":"刚才那句"}}]}
            """;

        var message = WeChatProtocol.ParseMessage(JsonDocument.Parse(json).RootElement);

        Assert.NotNull(message);
        Assert.Equal("g1", message.GroupId);
        Assert.Equal("[引用: 刚才那句]\n同意", message.Text);
    }

    [Fact]
    public void ParseMessage_VoiceKeepsTranscriptAndMedia()
    {
        var json = """
            {"message_id":"m3","from_user_id":"u1","item_list":[
              {"type":3,"voice_item":{"text":"晚上好","media":{"encrypt_query_param":"q"}}}]}
            """;

        var message = WeChatProtocol.ParseMessage(JsonDocument.Parse(json).RootElement);

        Assert.NotNull(message);
        Assert.Equal("[微信语音转写] 晚上好", message.Text);
        var media = Assert.Single(message.Media!);
        Assert.Equal(WeChatMediaKind.Voice, media.Kind);
        Assert.Equal("q", media.EncryptedQueryParam);
    }

    [Fact]
    public void ParseMessage_MediaWithoutDownloadHintIsIgnored()
    {
        var json = """
            {"message_id":"m4","from_user_id":"u1","item_list":[
              {"type":2,"image_item":{"file_name":"a.jpg"}}]}
            """;

        var message = WeChatProtocol.ParseMessage(JsonDocument.Parse(json).RootElement);

        Assert.NotNull(message);
        Assert.Empty(message.Media ?? []);
        Assert.Equal(string.Empty, message.Text);
    }

    [Fact]
    public void ParseMessage_RequiresIds()
    {
        Assert.Null(WeChatProtocol.ParseMessage(JsonDocument.Parse("""{"item_list":[]}""").RootElement));
        Assert.Null(WeChatProtocol.ParseMessage(JsonDocument.Parse("""{"message_id":"m","item_list":[]}""").RootElement));
    }

    [Fact]
    public void BuildTarget_DmAndGroup()
    {
        var dm = WeChatProtocol.ParseMessage(Message())!;
        Assert.Equal("wechat:dm:u1", WeChatProtocol.BuildTarget(dm));

        var group = WeChatProtocol.ParseMessage(JsonDocument.Parse(
            """{"message_id":"m5","from_user_id":"u1","group_id":"g1","item_list":[]}""").RootElement)!;
        Assert.Equal("wechat:group:g1", WeChatProtocol.BuildTarget(group));
    }

    [Fact]
    public void ParseTarget_RoundTrips()
    {
        Assert.Equal(("u1", false), WeChatProtocol.ParseTarget("wechat:dm:u1"));
        Assert.Equal(("g1", true), WeChatProtocol.ParseTarget("wechat:group:g1"));
        Assert.Throws<FormatException>(() => WeChatProtocol.ParseTarget("u1"));
    }

    [Fact]
    public void ContentDedupeKey_DependsOnSenderAndText()
    {
        var a = WeChatProtocol.ContentDedupeKey("u1", "你好");
        Assert.Equal(a, WeChatProtocol.ContentDedupeKey("u1", "你好"));
        Assert.NotEqual(a, WeChatProtocol.ContentDedupeKey("u2", "你好"));
        Assert.NotEqual(a, WeChatProtocol.ContentDedupeKey("u1", "再见"));
    }

    [Fact]
    public void SplitText_RespectsThresholdAndKeepsContent()
    {
        var text = string.Join("\n", Enumerable.Repeat("这是一行有点长的中文内容。", 300));

        var chunks = WeChatProtocol.SplitText(text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= WeChatProtocol.SplitThreshold));
        Assert.Equal(text.Replace("\n", string.Empty), string.Concat(chunks).Replace("\n", string.Empty));
    }

    private static JsonElement Message() => JsonDocument.Parse("""
        {"message_id":"m1","from_user_id":"u1","to_user_id":"bot","context_token":"ctx",
         "item_list":[{"type":1,"text_item":{"text":"你好"}}]}
        """).RootElement;
}
