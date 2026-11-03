using Shoyee.Notes.Obsidian;
using Xunit;

namespace Shoyee.Notes.Obsidian.Tests.Internal;

public sealed class VaultSearchTests
{
    [Fact]
    public void BuildSnippet_WindowsAroundQuery()
    {
        var line = new string('x', 400) + "命中" + new string('y', 400);

        var snippet = VaultSearch.BuildSnippet(line, "命中");

        Assert.Contains("命中", snippet);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
    }

    [Fact]
    public void ManagedSearch_MdFirstAndSkipsObsidianMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "shoyee-notes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(root, "notes"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".obsidian", "workspace.md"), "关键词 不该命中");
            File.WriteAllText(Path.Combine(root, "notes", "a.txt"), "关键词 文本");
            File.WriteAllText(Path.Combine(root, "notes", "b.md"), "关键词 笔记");

            var hits = VaultSearch.ManagedSearch(root, "关键词", 10);

            Assert.Equal(2, hits.Count);
            Assert.Equal(Path.Combine("notes", "b.md"), hits[0].Path);
            Assert.Equal(1, hits[0].Line);
            Assert.DoesNotContain(hits, h => h.Path.Contains(".obsidian"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 命中数远超 take 时也必须能收尾：以前收够条数就停止读 stdout，
    /// rg 写满 64KB 管道后阻塞、永不退出，整个工具调用挂死（2026-11-02 实测）。
    /// </summary>
    [Fact]
    public async Task RipgrepAsync_DrainsOutputBeyondTake()
    {
        if (RipgrepAvailable() is false)
        {
            return; // 环境里没有 rg：这条只验 dead-lock 回归，装不上就跳过
        }

        var root = Path.Combine(Path.GetTempPath(), "shoyee-rg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // rg 的 --max-count 是「每文件 3 条」，所以要靠文件数把 stdout 堆过 64KB 管道：
            // 200 个文件 × 3 条 × 两百来字节 ≈ 100KB+。
            var line = "命中 " + new string('x', 120);
            var body = string.Join('\n', Enumerable.Repeat(line, 200));
            for (var i = 0; i < 200; i++)
            {
                File.WriteAllText(Path.Combine(root, $"note-{i:D3}.md"), body);
            }

            var task = VaultSearch.RipgrepAsync(root, "命中", "*.md", take: 5, CancellationToken.None);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.Same(task, done);

            var hits = await task;
            Assert.Equal(5, hits.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool RipgrepAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, "rg")));
    }

    [Fact]
    public void ParseMatchLine_IgnoresNonMatchJson()
    {
        Assert.Null(VaultSearch.ParseMatchLine("""{"type":"begin"}"""));
        Assert.Null(VaultSearch.ParseMatchLine("not json"));
        var parsed = VaultSearch.ParseMatchLine(
            """{"type":"match","data":{"path":{"text":"/v/a.md"},"lines":{"text":"关键词"},"line_number":12}}""");
        Assert.Equal("/v/a.md", parsed?.Path);
        Assert.Equal(12, parsed?.Line);
    }
}
