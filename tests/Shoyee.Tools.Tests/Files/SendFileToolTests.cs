using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Files;
using Shoyee.Tools.Media;
using Xunit;

namespace Shoyee.Tools.Tests.Files;

/// <summary><c>send_file</c>：路径围栏、黑名单、出站附件收集。</summary>
public sealed class SendFileToolTests
{
    [Fact]
    public async Task MissingFile_Explains()
    {
        var tool = Tool(SendFileTool.Create(Config(NewRoot())));

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = "nope.txt" });

        Assert.Contains("文件不存在", result?.ToString());
    }

    [Fact]
    public async Task OutsideWorkspace_IsRejected()
    {
        var root = NewRoot();
        var outside = NewRoot();
        try
        {
            var file = Path.Combine(outside, "secret.txt");
            await File.WriteAllTextAsync(file, "secret");

            var tool = Tool(SendFileTool.Create(Config(root)));
            var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = file });

            Assert.Contains("只能发送工作区或笔记库", result?.ToString());
            Assert.Null(OutboundScope.Current);
        }
        finally
        {
            TryDelete(root);
            TryDelete(outside);
        }
    }

    [Fact]
    public async Task NotesRoot_IsAllowed()
    {
        var root = NewRoot();
        var notes = NewRoot();
        try
        {
            var file = Path.Combine(notes, "note.md");
            await File.WriteAllTextAsync(file, "# 笔记");

            var tool = Tool(SendFileTool.Create(Config(root), notes));
            var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = file });

            Assert.Contains("已附加文件", result?.ToString());
        }
        finally
        {
            TryDelete(root);
            TryDelete(notes);
        }
    }

    [Fact]
    public async Task DeniedPath_IsRejected()
    {
        var root = NewRoot();
        try
        {
            var file = Path.Combine(root, "assets", "images", "shot.png");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, [1, 2, 3]);

            var tool = Tool(SendFileTool.Create(Config(root)));
            var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = file });

            Assert.Contains("禁止访问", result?.ToString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task InsideWorkspace_RegistersOutboundAttachment()
    {
        var root = NewRoot();
        try
        {
            var file = Path.Combine(root, "report.pdf");
            await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);

            var collector = new OutboundAttachments();
            OutboundScope.Current = collector;
            try
            {
                var tool = Tool(SendFileTool.Create(Config(root)));
                var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = "report.pdf" });

                Assert.Contains("已附加文件", result?.ToString());
                var attachment = Assert.Single(collector.Items);
                Assert.Equal("report.pdf", attachment.FileName);
                Assert.True(File.Exists(attachment.Path), attachment.Path);
                Assert.Equal("application/pdf", attachment.Mime);
                Assert.Equal(Shoyee.Contracts.Domain.AttachmentKind.File, attachment.Kind);
                Assert.Equal(4, attachment.SizeBytes);
            }
            finally
            {
                OutboundScope.Current = null;
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Toolset_MountsInBothProfiles()
    {
        var root = NewRoot();
        try
        {
            var builder = new ToolsetBuilder(Config(root));

            foreach (var profile in new[] { ToolProfile.Work, ToolProfile.Roleplay })
            {
                var names = builder.Build(profile).Tools.OfType<AIFunction>().Select(static f => f.Name).ToList();
                Assert.Contains(SendFileTool.ToolName, names);
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static AIFunction Tool(ToolDescriptor descriptor) => (AIFunction)descriptor.Tool;

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "shoyee-sendfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // 清理失败不追究
        }
    }

    private static WorkspaceConfig Config(string root)
        => new(
            root,
            new ModelProfiles(
                new ModelEndpoint("http://localhost", "m", "KEY"),
                new ModelEndpoint("http://localhost", "s", "KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());
}
