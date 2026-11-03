using Shoyee.Contracts.Options;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Files;
using Xunit;

namespace Shoyee.Tools.Tests.Files;

public sealed class FileAccessToolsTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-tools-" + Guid.NewGuid().ToString("N"));

    public FileAccessToolsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task WriteThenRead_RoundTripsWithoutBom()
    {
        var config = Config();
        var tools = FileAccessTools.Create(config);
        var write = Function(tools, FileAccessTools.WriteToolName);
        var read = Function(tools, FileAccessTools.ReadToolName);
        var path = Path.Combine(_root, "notes", "a.md");

        var written = await write.InvokeAsync(new() { ["path"] = path, ["content"] = "你好" });
        Assert.Contains("已写入：", written?.ToString());
        Assert.Contains(path, written?.ToString());
        Assert.Equal([0xE4, 0xBD, 0xA0, 0xE5, 0xA5, 0xBD], await File.ReadAllBytesAsync(path));

        var text = await read.InvokeAsync(new() { ["path"] = path });
        Assert.Equal("你好", text?.ToString());
    }

    [Fact]
    public async Task Write_RefusesOverwriteByDefault()
    {
        var config = Config();
        var write = Function(FileAccessTools.Create(config), FileAccessTools.WriteToolName);
        var path = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(path, "old");

        var result = await write.InvokeAsync(new() { ["path"] = path, ["content"] = "new" });

        Assert.Contains("没覆盖", result?.ToString());
        Assert.Equal("old", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Grep_SkipsBinaryAndFindsText()
    {
        var config = Config();
        var grep = Function(FileAccessTools.Create(config), FileAccessTools.GrepToolName);
        await File.WriteAllTextAsync(Path.Combine(_root, "a.md"), "hello shoyee\n");
        await File.WriteAllBytesAsync(Path.Combine(_root, "b.bin"), [0, 1, 2, 3]);

        var result = await grep.InvokeAsync(new() { ["regexPattern"] = "shoyee", ["directory"] = _root });

        Assert.Contains("a.md:1:", result?.ToString());
        Assert.DoesNotContain("b.bin", result?.ToString());
    }

    [Fact]
    public async Task Write_ConfinedModeRejectsOutsideWorkspace()
    {
        var config = Config() with { Root = _root };
        var write = Function(FileAccessTools.Create(config, confineWrites: true), FileAccessTools.WriteToolName);
        var outside = Path.Combine(Path.GetTempPath(), "shoyee-outside-" + Guid.NewGuid().ToString("N") + ".txt");

        var result = await write.InvokeAsync(new() { ["path"] = outside, ["content"] = "x" });

        Assert.Contains("越界", result?.ToString());
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public void RealPath_ResolvesSymlinkTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var inside = Path.Combine(_root, "inside");
        Directory.CreateDirectory(inside);
        var link = Path.Combine(inside, "link");
        Directory.CreateSymbolicLink(link, outside);

        var resolved = RealPath.Resolve(Path.Combine(link, "x.txt"));

        Assert.Equal(Path.Combine(outside, "x.txt"), resolved);
    }

    private WorkspaceConfig Config()
        => new(
            _root,
            new ModelProfiles(new ModelEndpoint("http://localhost", "m", "KEY"), new ModelEndpoint("http://localhost", "s", "KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());

    private static Microsoft.Extensions.AI.AIFunction Function(
        IReadOnlyList<ToolDescriptor> tools,
        string name)
        => (Microsoft.Extensions.AI.AIFunction)tools.Single(t => t.Metadata.Name == name).Tool;
}
