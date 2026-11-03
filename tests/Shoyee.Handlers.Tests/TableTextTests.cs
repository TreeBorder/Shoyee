using Shoyee.Handlers.Commands;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary>表格排版：竖线/换行不拆列，空值写 —，时间统一本地格式。</summary>
public sealed class TableTextTests
{
    [Fact]
    public void Cell_EscapesPipesAndNewlines()
    {
        Assert.Equal("a｜b c", TableText.Cell("a|b\nc"));
    }

    [Fact]
    public void Cell_BlankBecomesDash()
    {
        Assert.Equal("—", TableText.Cell(null));
        Assert.Equal("—", TableText.Cell("  "));
    }

    [Fact]
    public void Time_NullBecomesDash()
    {
        Assert.Equal("—", TableText.Time(null));
    }

    [Fact]
    public void Table_RendersHeaderAndRows()
    {
        var table = TableText.Table(["A", "B"], [new[] { "1", "2" }, new[] { "3", "4" }]);

        Assert.Equal("| A | B |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |", table);
    }
}
