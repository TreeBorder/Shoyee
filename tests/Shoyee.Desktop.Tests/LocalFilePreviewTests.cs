using Shoyee.Desktop.ViewModels.Items;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>预览命令模板的拆分与占位符替换。</summary>
public sealed class LocalFilePreviewTests
{
    [Fact]
    public void ReplacesPathPlaceholderWithoutShell()
    {
        var path = Path.Combine(Path.GetTempPath(), "a b", "报告.xlsx");
        var parts = LocalFilePreview.BuildArguments("open -R \"{path}\"", path);

        Assert.Equal(["open", "-R", Path.GetFullPath(path)], [.. parts]);
    }

    [Fact]
    public void AppendsPathWhenTemplateHasNoPlaceholder()
    {
        var path = Path.Combine(Path.GetTempPath(), "a.txt");
        var parts = LocalFilePreview.BuildArguments("xdg-open", path);

        Assert.Equal(["xdg-open", Path.GetFullPath(path)], [.. parts]);
    }

    [Fact]
    public void SupportsSingleQuotesAndRepeatedPlaceholder()
    {
        var path = Path.Combine(Path.GetTempPath(), "x.txt");
        var parts = LocalFilePreview.BuildArguments("prog --in '{path}' --out {path}", path);

        Assert.Equal(["prog", "--in", Path.GetFullPath(path), "--out", Path.GetFullPath(path)], [.. parts]);
    }

    [Fact]
    public void EmptyTemplateProducesNoArguments()
        => Assert.Empty(LocalFilePreview.BuildArguments("   ", "x.txt"));
}
