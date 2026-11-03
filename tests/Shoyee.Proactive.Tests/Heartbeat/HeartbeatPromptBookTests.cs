using Shoyee.Proactive.Heartbeat;
using Xunit;

namespace Shoyee.Proactive.Tests.Heartbeat;

public sealed class HeartbeatPromptBookTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "shoyee-prompts-" + Guid.NewGuid().ToString("N")[..8]);

    public HeartbeatPromptBookTests() => Directory.CreateDirectory(_root);

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

    [Fact]
    public void MissingFile_ReturnsNull()
    {
        var book = new HeartbeatPromptBook(_root);

        Assert.Null(book.Resolve("text", 0));
    }

    [Fact]
    public void ReadsLevelFromWorkspaceFile()
    {
        var path = Path.Combine(_root, ".heartbeat", "prompts.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "Text": { "l1": "轻轻问一句", "l3": "说想他" },
              "Story": { "l1": "讲件小事" }
            }
            """);

        var book = new HeartbeatPromptBook(_root);

        Assert.Equal("轻轻问一句", book.Resolve("text", 0));
        Assert.Equal("说想他", book.Resolve("Text", 2));
        Assert.Null(book.Resolve("text", 1));       // 缺档位
        Assert.Null(book.Resolve("story", 4));      // 缺档位
        Assert.Null(book.Resolve("voice", 0));      // 类别不存在
    }

    [Fact]
    public void InvalidJson_FallsBackToNull()
    {
        var path = Path.Combine(_root, ".heartbeat", "prompts.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Null(new HeartbeatPromptBook(_root).Resolve("text", 0));
    }
}
