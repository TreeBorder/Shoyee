using Shoyee.Desktop.Conversations;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>真实会话快照的 C# 解析：工具行、附件、Markdown 清理与 RP 出图。</summary>
public sealed class TranscriptReaderTests
{
    [Fact]
    public void ReadsSecretarySessions()
    {
        var sessions = SecretaryTranscriptReader.ReadSessions();

        Assert.NotEmpty(sessions);
        Assert.Contains(sessions, session => session.Source == "Mattermost");
        Assert.Contains(sessions, session => session.Source == "微信");

        var messages = sessions.SelectMany(session => session.Messages).ToList();
        Assert.Contains(messages, message => message.IsTool && message.ToolName == "terminal");
        Assert.Contains(messages, message => message.IsReasoning && !string.IsNullOrWhiteSpace(message.Text));
        Assert.Contains(messages, message => message.IsTool && !string.IsNullOrWhiteSpace(message.Detail));
        Assert.Contains(messages, message => message.IsTool && !string.IsNullOrWhiteSpace(message.Result));
        Assert.Contains(
            sessions.First(session => session.Title == "语音识别链路").Messages,
            message => message.Attachments.Any(item => item.IsAudio));
        Assert.DoesNotContain(
            messages.Where(message => message.IsAssistant),
            message => message.Text.Contains("**", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsHaremTimelineWithPhotos()
    {
        var content = HaremTranscriptReader.Read();

        Assert.NotEmpty(content.Messages);
        Assert.Contains(content.Messages, message => message.IsUser);
        Assert.Contains(content.Messages, message => message.IsReasoning);
        Assert.Contains(content.Messages, message => message.IsTool && !string.IsNullOrWhiteSpace(message.Result));
        Assert.Contains(
            content.Messages,
            message => message.IsAssistant && message.Attachments.Any(item => item.IsImage));
        Assert.DoesNotContain(
            content.Messages,
            message => message.IsSystem && message.Text.Contains("系统消息", StringComparison.Ordinal));
        Assert.DoesNotContain(
            content.Messages,
            message => message.Text.Contains("InnerVoice", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitsBubblesOnEveryNewlineAndBreakTags()
    {
        var bubbles = RoleplayBubbleParser.Split("第一段\n第二段<br/>第三段\n\n第四段");

        Assert.Equal(["第一段", "第二段", "第三段", "第四段"], bubbles.Select(item => item.Text));
        Assert.All(bubbles, bubble => Assert.False(bubble.IsImage));
    }

    [Fact]
    public void KeepsCodeBlockIndentationWhenSplitting()
    {
        var bubbles = RoleplayBubbleParser.Split("看代码：\n\n```csharp\nvar a = 1;\n\nvar b = 2;\n```\n\n结束");

        Assert.Equal(3, bubbles.Count);
        Assert.Contains("var a = 1;", bubbles[1].Text, StringComparison.Ordinal);
        Assert.Contains("var b = 2;", bubbles[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractsMarkdownImagesIntoOwnBubbles()
    {
        var bubbles = RoleplayBubbleParser.Split(
            "给你看照片\n\n![图](/tmp/a.png)\n\n第二张：![图2](https://example.com/b.jpg \"标题\") 好可爱\n\n![图3](file:///tmp/c.png)");

        Assert.Collection(
            bubbles,
            bubble => Assert.Equal("给你看照片", bubble.Text),
            bubble => Assert.Equal("/tmp/a.png", bubble.ImageSource),
            bubble => Assert.Equal("第二张：", bubble.Text),
            bubble => Assert.Equal("https://example.com/b.jpg", bubble.ImageSource),
            bubble => Assert.Equal("好可爱", bubble.Text),
            bubble => Assert.Equal("file:///tmp/c.png", bubble.ImageSource));
        Assert.DoesNotContain(bubbles, bubble => bubble.Text.Contains("![", StringComparison.Ordinal));
    }
}
