using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.ViewModels.Items;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>正文里的本地文件链接拆泡：一个文件一个气泡，前后文字各成一段。</summary>
public sealed class LocalFileLinkParserTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "shoyee-desktop-bubbles",
        Guid.NewGuid().ToString("N"));

    public LocalFileLinkParserTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SplitsLinkInTheMiddle()
    {
        var path = CreateFile("report.md");

        var bubbles = LocalFileLinkParser.Split($"报告在这：[报告]({path})，重点看第三节。");

        Assert.Equal(3, bubbles.Count);
        Assert.Equal("报告在这：", bubbles[0].Text);
        Assert.True(bubbles[1].IsFile);
        Assert.Equal(path, bubbles[1].FilePath);
        Assert.Equal("报告", bubbles[1].FileLabel);
        Assert.Equal("，重点看第三节。", bubbles[2].Text);
    }

    [Fact]
    public void SplitsLinkAtTheEnd()
    {
        var path = CreateFile("report.md");

        var bubbles = LocalFileLinkParser.Split($"改好了：[报告]({path})");

        Assert.Equal(2, bubbles.Count);
        Assert.Equal("改好了：", bubbles[0].Text);
        Assert.Equal(path, bubbles[1].FilePath);
    }

    [Fact]
    public void KeepsRemoteRelativeAndMissingLinksInText()
    {
        var missing = Path.Combine(_root, "missing.md");
        var text = $"看 [官网](https://example.com/a) 和 [相对](notes/a.md) 以及 [不存在]({missing})";

        var bubbles = LocalFileLinkParser.Split(text);

        var only = Assert.Single(bubbles);
        Assert.False(only.IsFile);
        Assert.Contains("官网", only.Text);
        Assert.Contains("notes/a.md", only.Text);
    }

    [Fact]
    public void DoesNotSplitInsideCodeFence()
    {
        var path = CreateFile("sample.md");
        var text = $"示例：\n\n```md\n[文件]({path})\n```\n";

        var bubbles = LocalFileLinkParser.Split(text);

        var only = Assert.Single(bubbles);
        Assert.False(only.IsFile);
        Assert.Contains("```md", only.Text);
    }

    [Fact]
    public void AcceptsFileUriEvenWhenMissing()
    {
        var missing = Path.Combine(_root, "gone.txt");

        var bubbles = LocalFileLinkParser.Split($"[旧文件]({new Uri(missing).AbsoluteUri})");

        var file = Assert.Single(bubbles);
        Assert.True(file.IsFile);
        Assert.Equal(missing, file.FilePath);
    }

    [Fact]
    public void ProjectsFileBubbleBetweenTextBubbles()
    {
        var path = CreateFile("plan.md");
        var items = DaemonProjection.ToBubbleItems(
            $"计划：[计划]({path})，请确认。",
            "10:00");

        Assert.Equal(3, items.Count);
        Assert.True(items[0].IsAssistant);
        Assert.Equal("计划：", items[0].Text);

        var file = items[1];
        Assert.True(file.IsFileBubble);
        Assert.Equal(path, file.FilePath);
        Assert.Equal("计划", file.FileName);
        Assert.True(file.CanOpenFile);
        Assert.Equal("点击打开", file.FileActionTip);

        Assert.True(items[2].IsAssistant);
        Assert.Equal("，请确认。", items[2].Text);
    }

    [Fact]
    public void ProjectsLocalImageAsImageBubble()
    {
        var path = CreateFile("photo.png", [0x89, 0x50, 0x4E, 0x47]);

        var items = DaemonProjection.ToBubbleItems($"看这张：![照片]({path})", "10:00");

        var image = items.Last();
        Assert.True(image.IsImage);
        Assert.Equal(path, image.ImageSource);
        Assert.Equal(path, image.FilePath);
    }

    [Fact]
    public void MissingFileBubbleStillRendersWithHint()
    {
        var missing = Path.Combine(_root, "gone.md");

        var items = DaemonProjection.ToBubbleItems($"[旧文件]({new Uri(missing).AbsoluteUri})", "10:00");

        var file = Assert.Single(items);
        Assert.True(file.IsFileBubble);
        Assert.False(file.CanOpenFile);
        Assert.Equal("文件已移动或删除", file.FileActionTip);
    }

    [Theory]
    [InlineData("/tmp/a.sh", true)]
    [InlineData("/tmp/a.command", true)]
    [InlineData("/tmp/a.pdf", false)]
    [InlineData("/tmp/a.md", false)]
    public void RevealsRiskyExtensionsInsteadOfOpening(string path, bool revealOnly)
        => Assert.Equal(revealOnly, LocalFileOpener.ShouldRevealOnly(path));

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content ?? [1, 2, 3]);
        return path;
    }
}
