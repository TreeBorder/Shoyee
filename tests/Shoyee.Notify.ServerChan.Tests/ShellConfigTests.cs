using System.Net;
using System.Text;
using Shoyee.Notify.ServerChan;
using Xunit;

namespace Shoyee.Notify.ServerChan.Tests;

public sealed class ShellConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shoyee-sc-" + Guid.NewGuid().ToString("N")[..8]);

    public ShellConfigTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Read_ParsesExportQuotesAndComments()
    {
        var path = Write("""
            # Server酱
            SCT_UID=12345
            export SCT_SENDKEY='SCT123abc'
            OTHER="x # y" # trailing
            EMPTY=
            """);

        var values = ShellConfig.Read(path);

        Assert.Equal("12345", values["SCT_UID"]);
        Assert.Equal("SCT123abc", values["SCT_SENDKEY"]);
        Assert.Equal("x # y", values["OTHER"]);
        Assert.Equal(string.Empty, values["EMPTY"]);
    }

    [Fact]
    public void Read_MissingFile_Throws()
    {
        var path = Path.Combine(_dir, "nope.sh");
        Assert.Throws<FileNotFoundException>(() => ShellConfig.Read(path));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string content)
    {
        var path = Path.Combine(_dir, "config.sh");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
}
