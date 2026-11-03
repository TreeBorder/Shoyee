using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Sessions.Turns;
using Xunit;

namespace Shoyee.Sessions.Tests.Turns;

/// <summary>出站附件挂到最后一条可见助手正文上（不挂工具行、不碰历史轮次）。</summary>
public sealed class TurnAttachmentsTests
{
    private static readonly MessageAttachment File = new(
        "/tmp/voice.wav",
        "voice.wav",
        "audio/wav",
        10,
        AttachmentKind.Audio);

    [Fact]
    public void AttachesToLastAssistantText()
    {
        var first = new ChatMessage(ChatRole.Assistant, "先说一句");
        var tool = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "voice_speak")]);
        var last = new ChatMessage(ChatRole.Assistant, "最后一句");

        var target = TurnAttachments.AttachToLastAssistantText([first, tool, last], [File]);

        Assert.Same(last, target);
        Assert.Null(first.AdditionalProperties);
        var stored = Assert.IsAssignableFrom<IReadOnlyList<MessageAttachment>>(
            target!.AdditionalProperties![ChatMessageMarkers.Attachments]);
        Assert.Same(File, Assert.Single(stored));
    }

    [Fact]
    public void IgnoresMessagesBeforeStartIndex()
    {
        var old = new ChatMessage(ChatRole.Assistant, "上一轮");
        var toolOnly = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "send_file")]);

        var target = TurnAttachments.AttachToLastAssistantText([old, toolOnly], [File], startIndex: 1);

        Assert.Null(target);
        Assert.Null(old.AdditionalProperties);
    }

    [Fact]
    public void IgnoresUserAndEmptyText()
    {
        var user = new ChatMessage(ChatRole.User, "你好");
        var empty = new ChatMessage(ChatRole.Assistant, [new TextContent("  ")]);

        Assert.Null(TurnAttachments.AttachToLastAssistantText([user, empty], [File]));
    }

    [Fact]
    public void NoAttachments_DoesNothing()
    {
        var message = new ChatMessage(ChatRole.Assistant, "你好");

        Assert.Null(TurnAttachments.AttachToLastAssistantText([message], null));
        Assert.Null(message.AdditionalProperties);
    }
}
