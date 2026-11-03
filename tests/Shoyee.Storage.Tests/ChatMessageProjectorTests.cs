using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Converters;
using Xunit;

namespace Shoyee.Storage.Tests;

/// <summary>框架消息 → 存储列的投影规则（docs/架构与数据模型.md §3.3）。</summary>
public sealed class ChatMessageProjectorTests
{
    [Fact]
    public void 普通文本_投影为Text且正文可检索()
    {
        var message = new ChatMessage(ChatRole.User, "今天打卡完成");

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.Text, projection.Kind);
        Assert.Equal("user", projection.Role);
        Assert.Equal("今天打卡完成", projection.Content);
        Assert.True(ChatMessageProjector.IsSearchable(projection));
    }

    [Fact]
    public void 工具结果_不写正文且不可检索()
    {
        var message = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "ok")]);

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.ToolResult, projection.Kind);
        Assert.Equal(string.Empty, projection.Content);
        Assert.False(ChatMessageProjector.IsSearchable(projection));
        Assert.True(ChatMessageProjector.ShouldPersist(projection));
        Assert.Equal("call_1", projection.ToolCallId);
        Assert.Equal("ok", projection.ToolResult);
    }

    [Fact]
    public void 工具结果_非字符串返回序列化后保留()
    {
        var message = new ChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call_1", new Dictionary<string, object?> { ["count"] = 3 }),
        ]);

        var projection = ChatMessageProjector.Project(message);

        Assert.Contains("count", projection.ToolResult);
    }

    [Fact]
    public void 工具调用_带工具名_调用前正文可检索()
    {
        var message = new ChatMessage(ChatRole.Assistant, [
            new TextContent("我先查一下文件"),
            new FunctionCallContent("call_2", "file_access_read", new Dictionary<string, object?> { ["fileName"] = "a.md" }),
        ]);

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.ToolCall, projection.Kind);
        Assert.Equal("file_access_read", projection.ToolName);
        Assert.Equal("我先查一下文件", projection.Content);
        Assert.True(ChatMessageProjector.IsSearchable(projection));
        Assert.Equal("call_2", projection.ToolCallId);
        Assert.Contains("a.md", projection.ToolArguments);
    }

    [Fact]
    public void 文本_同时保留思考()
    {
        var message = new ChatMessage(ChatRole.Assistant, [
            new TextReasoningContent("先想一下"),
            new TextContent("答案"),
        ]);

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.Text, projection.Kind);
        Assert.Equal("答案", projection.Content);
        Assert.Equal("先想一下", projection.Thinking);
    }

    [Fact]
    public void 摘要标记_投影为Summary且不检索()
    {
        var message = new ChatMessage(ChatRole.Assistant, "[Summary]\n之前的对话……")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageProjector.SummaryMarker] = true,
            },
        };

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.Summary, projection.Kind);
        Assert.False(ChatMessageProjector.IsSearchable(projection));
    }

    [Fact]
    public void 主动发言标记_投影为Spoken()
    {
        var message = new ChatMessage(ChatRole.Assistant, "想你了")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageProjector.SpokenMarker] = true,
            },
        };

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.Spoken, projection.Kind);
        Assert.True(ChatMessageProjector.IsSearchable(projection));
    }

    [Fact]
    public void 系统注入标记_落独立种类_默认可隐藏且不检索()
    {
        var message = new ChatMessage(ChatRole.User, "[系统消息]现在是心跳时刻[/]")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageMarkers.SystemInput] = true,
            },
        };

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.SystemInput, projection.Kind);
        Assert.Equal("[系统消息]现在是心跳时刻[/]", projection.Content);
        Assert.False(ChatMessageProjector.IsSearchable(projection));
        Assert.True(ChatMessageProjector.ShouldPersist(projection));
    }

    [Fact]
    public void 附件与展示正文_来自消息标记()
    {
        var attachments = new MessageAttachment[]
        {
            new("/tmp/inbox/a.png", "a.png", "image/png", 10, AttachmentKind.Image, 1200),
        };
        var message = new ChatMessage(ChatRole.User, "[附件] /tmp/inbox/a.png (image/png)\n看图")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageMarkers.DisplayText] = "看图",
                [ChatMessageMarkers.Attachments] = attachments,
            },
        };

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.Text, projection.Kind);
        Assert.Equal("看图", projection.Content);
        var attachment = Assert.Single(projection.Attachments!);
        Assert.Equal("/tmp/inbox/a.png", attachment.Path);
        Assert.Equal(1200, attachment.DurationMs);
    }

    [Fact]
    public void 只有推理内容_不落行()
    {
        var message = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("思考过程")]);

        var projection = ChatMessageProjector.Project(message);

        Assert.Equal(MessageKind.Unknown, projection.Kind);
        Assert.False(ChatMessageProjector.ShouldPersist(projection));
    }

    [Fact]
    public void 消息JSON往返保留工具调用内容()
    {
        var message = new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent("call_3", "session_search", new Dictionary<string, object?> { ["query"] = "周报" }),
        ]);

        var restored = ChatMessageCodec.Deserialize(ChatMessageCodec.Serialize(message));

        var call = Assert.Single(restored.Contents.OfType<FunctionCallContent>());
        Assert.Equal("session_search", call.Name);
        Assert.Equal("call_3", call.CallId);
    }
}
