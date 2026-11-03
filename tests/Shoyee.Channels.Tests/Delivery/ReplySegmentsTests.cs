using Shoyee.Contracts.Text;
using Xunit;

namespace Shoyee.Channels.Tests.Delivery;

/// <summary>RP 正文按单个换行切段，代码块不拆。</summary>
public sealed class ReplySegmentsTests
{
    [Fact]
    public void SplitsOnEveryNewlineAndBreakTag()
    {
        var segments = ReplySegments.Split("第一段\n第二段<br/>第三段\n\n第四段");

        Assert.Equal(["第一段", "第二段", "第三段", "第四段"], segments);
    }

    [Fact]
    public void KeepsCodeFenceTogether()
    {
        var segments = ReplySegments.Split("看代码：\n```csharp\nvar a = 1;\nvar b = 2;\n```\n结束");

        Assert.Equal(
            ["看代码：", "```csharp\nvar a = 1;\nvar b = 2;\n```", "结束"],
            segments);
    }

    [Fact]
    public void IgnoresEmptyLines()
    {
        Assert.Equal(["在"], ReplySegments.Split("\n\n在\n\n"));
        Assert.Empty(ReplySegments.Split("  \n\t\n"));
    }
}
