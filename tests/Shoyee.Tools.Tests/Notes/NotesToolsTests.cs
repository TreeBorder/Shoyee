using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Notes;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Notes;
using Xunit;

namespace Shoyee.Tools.Tests.Notes;

public sealed class NotesToolsTests
{
    [Fact]
    public void Unconfigured_ProducesNoTools()
    {
        var provider = new FakeNotesProvider(configured: false);

        Assert.Empty(NotesTools.Create(provider));
    }

    [Fact]
    public void LocalProvider_GetsSearchAndRead_NoWrite()
    {
        var names = Names(NotesTools.Create(new FakeNotesProvider(configured: true)));

        Assert.Equal([NotesTools.SearchToolName, NotesTools.ReadToolName], names);
    }

    [Fact]
    public void WriterProvider_AddsWriteTool_WorkOnlyAndApprovalWrapped()
    {
        var remote = new FakeWriterNotesProvider();
        var builder = new ToolsetBuilder(Config(), new ToolsetServices(Notes: remote));

        var work = builder.Build(ToolProfile.Work).Tools;
        var roleplay = builder.Build(ToolProfile.Roleplay).Tools;

        Assert.Contains(NotesTools.WriteToolName, Names(work));
        Assert.Contains(NotesTools.SearchToolName, Names(roleplay));
        Assert.DoesNotContain(NotesTools.WriteToolName, Names(roleplay));
        Assert.IsType<ApprovalRequiredAIFunction>(
            work.Single(t => t is AIFunction f && f.Name == NotesTools.WriteToolName));
    }

    [Fact]
    public async Task Read_ReturnsBodyFromProvider()
    {
        var tool = Tool(NotesTools.Create(new FakeNotesProvider(configured: true)), NotesTools.ReadToolName);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["id"] = "项目/Shoyee.md" });

        Assert.Contains("# Shoyee", result?.ToString());
        Assert.Contains("path: /vault/项目/Shoyee.md", result?.ToString());
        Assert.Contains("正文内容", result?.ToString());
    }

    [Fact]
    public async Task Read_UnknownId_ReportsNotFound()
    {
        var tool = Tool(NotesTools.Create(new FakeNotesProvider(configured: true)), NotesTools.ReadToolName);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["id"] = "不存在.md" });

        Assert.Contains("没找到", result?.ToString());
    }

    private static AIFunction Tool(IReadOnlyList<ToolDescriptor> descriptors, string name)
        => (AIFunction)descriptors.Single(d => d.Metadata.Name == name).Tool;

    private static List<string> Names(IReadOnlyList<ToolDescriptor> descriptors)
        => [.. descriptors.Select(static d => d.Metadata.Name)];

    private static List<string> Names(IReadOnlyList<AITool> tools)
        => [.. tools.OfType<AIFunction>().Select(static f => f.Name)];

    private static WorkspaceConfig Config()
        => new(
            Path.Combine(Path.GetTempPath(), "shoyee-notes-" + Guid.NewGuid().ToString("N")),
            new ModelProfiles(
                new ModelEndpoint("http://localhost", "m", "KEY"),
                new ModelEndpoint("http://localhost", "s", "KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());

    private class FakeNotesProvider(bool configured) : INotesProvider
    {
        public virtual string Name => "fake";

        public bool Configured => configured;

        public Task<IReadOnlyList<NoteHit>> SearchAsync(
            string query,
            int take = 10,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NoteHit>>(
                [new NoteHit(new NoteRef(Name, "项目/Shoyee.md", "Shoyee"), "…正文内容…")]);

        public Task<NoteDocument?> ReadAsync(NoteRef note, CancellationToken cancellationToken = default)
            => Task.FromResult(
                note.Id == "项目/Shoyee.md"
                    ? new NoteDocument(
                        note with { Path = "/vault/" + note.Id },
                        "Shoyee",
                        "正文内容",
                        false,
                        DateTimeOffset.UnixEpoch)
                    : null);
    }

    private sealed class FakeWriterNotesProvider : FakeNotesProvider, INotesWriter
    {
        public FakeWriterNotesProvider()
            : base(configured: true)
        {
        }

        public override string Name => "notion";

        public Task<NoteRef> CreateAsync(NoteDraft draft, CancellationToken cancellationToken = default)
            => Task.FromResult(new NoteRef(Name, "new-page", draft.Title));

        public Task AppendAsync(NoteRef note, string markdown, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task OverwriteAsync(NoteRef note, string markdown, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
