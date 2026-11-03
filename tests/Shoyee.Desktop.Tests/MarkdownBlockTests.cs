using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Shoyee.Desktop;
using Shoyee.Desktop.Controls;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>气泡 Markdown 渲染：只验证聊天里常见的行内与块级写法都被解析掉。</summary>
[Collection("Avalonia")]
public sealed class MarkdownBlockTests
{
    public MarkdownBlockTests()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
    }


    [Fact]
    public void StreamingUpdatesSingleTextBlockInPlace()
    {
        var block = new MarkdownBlock
        {
            Markdown = "第一段",
            IsStreaming = true,
        };

        var text = Assert.IsType<SelectableTextBlock>(Assert.Single(block.Children));
        Assert.Equal("第一段", text.Text);

        block.Markdown = "第一段\n第二段";
        Assert.Same(text, Assert.Single(block.Children));
        Assert.Equal("第一段\n第二段", text.Text);

        // 轮末切回完整渲染由节流定时器完成，这里只确认状态已切走
        block.IsStreaming = false;
        Assert.False(block.IsStreaming);
    }

    [Fact]
    public void ParsesBoldInlineCodeAndStrikethrough()
    {
        var block = new MarkdownBlock
        {
            Markdown = "这是 **加粗** 和 `code` 以及 ~~删除~~ 文本",
        };

        var text = Assert.IsType<SelectableTextBlock>(Assert.Single(block.Children));
        var runs = text.Inlines!.OfType<Run>().ToList();

        Assert.Contains(runs, run => run.Text == "加粗" && run.FontWeight == FontWeight.Bold);
        Assert.Contains(runs, run => run.Text == "code" && run.FontFamily!.Name.Contains("Mono"));
        Assert.Contains(runs, run => run.Text == "删除" && run.TextDecorations is not null);
        Assert.DoesNotContain(runs, run => run.Text!.Contains("**"));
        Assert.DoesNotContain(runs, run => run.Text!.Contains("`"));
    }

    [Fact]
    public void ParsesCodeFenceHeadingAndList()
    {
        var block = new MarkdownBlock
        {
            Markdown = "# 标题\n\n- 第一项\n- 第二项\n\n```cs\nvar x = 1;\n```",
        };

        Assert.Equal(4, block.Children.Count);

        var heading = Assert.IsType<SelectableTextBlock>(block.Children[0]);
        Assert.True(heading.FontSize > block.FontSize);

        var first = Assert.IsType<SelectableTextBlock>(block.Children[1]);
        Assert.Contains("• 第一项", Runs(first));

        var code = Assert.IsType<Border>(block.Children[3]);
        var codeText = Assert.IsType<SelectableTextBlock>(code.Child);
        Assert.Equal("var x = 1;", codeText.Text);
        Assert.Contains("Mono", codeText.FontFamily!.Name);
    }

    [Fact]
    public void RendersLinkLabelWithoutUrl()
    {
        var block = new MarkdownBlock { Markdown = "见 [文档](https://example.com/a) 说明" };

        var text = Assert.IsType<SelectableTextBlock>(Assert.Single(block.Children));
        var joined = Runs(text);
        Assert.Contains("文档", joined);
        Assert.DoesNotContain("https://example.com", joined);
    }

    [Fact]
    public void RendersMarkdownTable()
    {
        var block = new MarkdownBlock
        {
            Markdown = """
                | 项目 | 状态 |
                | :--- | ---: |
                | 磁盘 | 正常 |
                | 内存 | **偏高** |
                """,
        };

        var border = Assert.IsType<Border>(Assert.Single(block.Children));
        var grid = Assert.IsType<Grid>(border.Child);
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(3, grid.RowDefinitions.Count);

        var header = Assert.IsType<Border>(grid.Children[0]);
        var headerText = Assert.IsType<SelectableTextBlock>(header.Child);
        Assert.Equal("项目", Runs(headerText));
        Assert.Equal(FontWeight.Bold, headerText.FontWeight);

        var last = Assert.IsType<Border>(grid.Children[^1]);
        var lastText = Assert.IsType<SelectableTextBlock>(last.Child);
        Assert.Equal("偏高", Runs(lastText));
        Assert.Contains(lastText.Inlines!.OfType<Run>(), run => run.FontWeight == FontWeight.Bold);
        Assert.Equal(TextAlignment.Right, lastText.TextAlignment);
    }

    private static string Runs(SelectableTextBlock block)
        => string.Concat(block.Inlines!.OfType<Run>().Select(run => run.Text));
}
