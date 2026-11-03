using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.ViewModels.Items;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>气泡复制：纯文本与 Markdown 原文，以及剪贴板不可用时的提示。</summary>
public sealed class ChatMessageItemTests
{
    [Fact]
    public void CopiesPlainTextWithoutMarkdownMarkers()
    {
        var item = new ChatMessageItem
        {
            Kind = ChatEntryKind.Assistant,
            Text = "**重点**\n\n- 第一项\n- [链接](https://example.com)",
        };

        Assert.True(item.CanCopy);
        Assert.Equal("**重点**\n\n- 第一项\n- [链接](https://example.com)", item.MarkdownText);
        Assert.DoesNotContain("**", item.PlainText, StringComparison.Ordinal);
        Assert.Contains("重点", item.PlainText, StringComparison.Ordinal);
        Assert.Contains("第一项", item.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRowsAreNotCopyable()
    {
        var item = new ChatMessageItem
        {
            Kind = ChatEntryKind.Tool,
            Text = "df -h",
        };

        Assert.False(item.CanCopy);
    }

    [Fact]
    public void CopyFailsGracefullyWithoutClipboard()
    {
        ClipboardService.Attach(null);
        var item = new ChatMessageItem
        {
            Kind = ChatEntryKind.User,
            Text = "你好",
        };

        item.CopyTextCommand.Execute(null);

        Assert.Equal("复制失败", item.CopyStatus);
        Assert.True(item.HasCopyStatus);
    }

    [Fact]
    public void StreamedTextConcatenatesInOrder()
    {
        var item = new ChatMessageItem { Kind = ChatEntryKind.Assistant };

        item.Append("你");
        item.Append("好");
        Assert.Equal("你好", item.Text);

        // 外部整体替换后继续追加，不能被旧缓冲污染
        item.Text = "重置";
        item.Append("！");
        Assert.Equal("重置！", item.Text);
    }

    [Fact]
    public void ReasoningStreamIsCapped()
    {
        var item = new ChatMessageItem { Kind = ChatEntryKind.Reasoning };
        var chunk = new string('思', 1_000);

        for (var i = 0; i < 100; i++)
        {
            item.Append(chunk);
        }

        Assert.StartsWith("思", item.Text, StringComparison.Ordinal);
        Assert.Contains("思考过长", item.Text, StringComparison.Ordinal);
        Assert.True(
            item.Text.Length < ConversationText.MaxReasoningChars + 64,
            $"思考没有截断：{item.Text.Length} 字");

        // 折叠时的单行预览不能随正文一起变长
        Assert.True(item.Preview.Length <= 111);
    }
}
