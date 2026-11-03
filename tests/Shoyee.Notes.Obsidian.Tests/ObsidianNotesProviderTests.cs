using Shoyee.Contracts.Options;
using Xunit;

namespace Shoyee.Notes.Obsidian.Tests;

public sealed class ObsidianNotesProviderTests
{
    [Fact]
    public async Task Unconfigured_IsNotConfiguredAndReturnsNoHits()
    {
        var provider = new ObsidianNotesProvider(null, Config(Path.GetTempPath()));

        Assert.Equal("obsidian", provider.Name);
        Assert.False(provider.Configured);
        Assert.Null(((ILocalNotesFiles)provider).Root);
        Assert.Null(((ILocalNotesFiles)provider).DefaultDirectory);
        Assert.Empty(await provider.SearchAsync("任何"));
    }

    [Fact]
    public void Configured_ResolvesRelativeRootAndDefaultDirectory()
    {
        var workspace = NewTempDirectory();
        try
        {
            var vault = Path.Combine(workspace, "vault");
            Directory.CreateDirectory(vault);

            var provider = new ObsidianNotesProvider(
                new ObsidianNotesOptions("vault", "shoyee"),
                Config(workspace));

            Assert.True(provider.Configured);
            Assert.Equal(vault, ((ILocalNotesFiles)provider).Root);
            Assert.Equal(Path.Combine(vault, "shoyee"), ((ILocalNotesFiles)provider).DefaultDirectory);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Search_FindsNotesAndReturnsOpaqueRef()
    {
        var workspace = NewTempDirectory();
        try
        {
            var vault = Path.Combine(workspace, "vault");
            Directory.CreateDirectory(Path.Combine(vault, "项目"));
            File.WriteAllText(Path.Combine(vault, "项目", "Shoyee.md"), "Shoyee 是她的名字。");
            File.WriteAllText(Path.Combine(vault, "其他.md"), "无关内容。");

            var provider = new ObsidianNotesProvider(new ObsidianNotesOptions(vault), Config(workspace));

            var hits = await provider.SearchAsync("Shoyee");

            var hit = Assert.Single(hits, h => h.Ref.Id == Path.Combine("项目", "Shoyee.md"));
            Assert.Equal("obsidian", hit.Ref.Provider);
            Assert.Equal("Shoyee", hit.Ref.Title);
            Assert.Equal(Path.Combine(vault, "项目", "Shoyee.md"), hit.Ref.Path);
            Assert.Contains("Shoyee", hit.Snippet);
            Assert.StartsWith("L1: ", hit.Snippet);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Read_ReturnsBodyAndRejectsEscape()
    {
        var workspace = NewTempDirectory();
        try
        {
            var vault = Path.Combine(workspace, "vault");
            Directory.CreateDirectory(Path.Combine(vault, "项目"));
            File.WriteAllText(Path.Combine(vault, "项目", "Shoyee.md"), "正文内容");
            File.WriteAllText(Path.Combine(workspace, "outside.md"), "不该读到");

            var provider = new ObsidianNotesProvider(new ObsidianNotesOptions(vault), Config(workspace));

            var document = await provider.ReadAsync(new NoteRef("obsidian", Path.Combine("项目", "Shoyee.md")));

            Assert.NotNull(document);
            Assert.Equal("Shoyee", document!.Title);
            Assert.Equal(Path.Combine(vault, "项目", "Shoyee.md"), document.Ref.Path);
            Assert.Equal("正文内容", document.Content);
            Assert.False(document.Truncated);

            Assert.Null(await provider.ReadAsync(new NoteRef("obsidian", Path.Combine("..", "outside.md"))));
            Assert.Null(await provider.ReadAsync(new NoteRef("obsidian", "不存在.md")));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string NewTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "shoyee-notes-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static WorkspaceConfig Config(string root)
        => new(
            root,
            new ModelProfiles(
                new ModelEndpoint("http://localhost:1/v1", "main-model", "MAIN_KEY"),
                new ModelEndpoint("http://localhost:1/v1", "small-model", "SMALL_KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());
}
