using Shoyee.Tools.Files;
using Xunit;

namespace Shoyee.Tools.Tests.Files;

public sealed class FileEditorTests
{
    [Fact]
    public void ApplyReplace_RequiresUniqueMatchWithoutReplaceAll()
    {
        Assert.Throws<ArgumentException>(() => FileEditor.ApplyReplace("aa", "a", "b", false));
        var (content, count) = FileEditor.ApplyReplace("aa", "a", "b", true);
        Assert.Equal("bb", content);
        Assert.Equal(2, count);
    }

    [Fact]
    public void ApplyReplaceLines_HonoursExpectedLine()
    {
        Assert.Throws<ArgumentException>(() => FileEditor.ApplyReplaceLines(
            "one\ntwo\n",
            [new FileLineEdit(2, "TWO\n", "wrong\n")]));

        var content = FileEditor.ApplyReplaceLines(
            "one\ntwo\n",
            [new FileLineEdit(2, "TWO\n", "two\n")]);
        Assert.Equal("one\nTWO\n", content);
    }

    [Fact]
    public void SliceLines_ClampsEnd()
    {
        var lines = FileEditor.SliceLines("a\nb\nc\n", 2, 99);
        Assert.Equal(["b\n", "c\n"], lines);
    }
}
