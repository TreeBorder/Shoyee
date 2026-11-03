using System.Diagnostics;
using System.Text.Json;

namespace Shoyee.Notes.Obsidian;

/// <summary>
/// vault 检索（<c>notes_search</c> 的执行体）：优先 <c>rg --json --fixed-strings</c>
/// （用户输入整串字面匹配，不进查询语法），rg 不在 PATH / 出错时退化为托管扫描。
/// 片段围绕命中位置自己开窗（FTS5 <c>snippet()</c> 在 trigram 下不可靠，见 notes）。
/// md 优先，再补其他文件；单文件最多 3 条。
/// </summary>
internal static class VaultSearch
{
    /// <summary>单个文件最多取几条命中（防一篇长文刷屏）。</summary>
    private const int MaxMatchesPerFile = 3;

    /// <summary>片段最大长度（字符）。</summary>
    private const int SnippetCap = 200;

    /// <summary>托管扫描的文件上限（防超大 vault 拖死）。</summary>
    private const int ManagedScanFileLimit = 5000;

    /// <summary>单趟 rg 的超时；超时就杀掉子进程、交给托管扫描兜底（vault 在 iCloud 上时偶尔会拖住）。</summary>
    private static readonly TimeSpan RipgrepTimeout = TimeSpan.FromSeconds(20);

    /// <summary>检索 vault：返回（相对 vault 根的）路径 + 命中片段，最多 <paramref name="take"/> 条。</summary>
    public static async Task<IReadOnlyList<VaultMatch>> SearchAsync(
        string vaultRoot,
        string query,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(vaultRoot) || !Directory.Exists(vaultRoot))
        {
            return [];
        }

        take = Math.Clamp(take, 1, 50);
        List<VaultMatch> hits;
        try
        {
            hits = [.. await RipgrepAsync(vaultRoot, query, "*.md", take, cancellationToken).ConfigureAwait(false)];
            if (hits.Count < take)
            {
                hits.AddRange(await RipgrepAsync(vaultRoot, query, "!*.md", take - hits.Count, cancellationToken)
                    .ConfigureAwait(false));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hits = [.. ManagedSearch(vaultRoot, query, take)];
        }

        return hits;
    }

    /// <summary>ripgrep 单趟：逐行解析 <c>--json</c> 的 match 行，命中行自开窗做片段。</summary>
    public static async Task<IReadOnlyList<VaultMatch>> RipgrepAsync(
        string vaultRoot,
        string query,
        string glob,
        int take,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "rg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
                 {
                     "--json", "--fixed-strings", "--ignore-case",
                     "--max-count", MaxMatchesPerFile.ToString(),
                     "--glob", glob,
                     "--glob", "!.obsidian",
                     "-e", query,
                     vaultRoot,
                 })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return [];
        }

        // stderr 必须先起读取：等 stdout 读完才读 stderr，rg 把 stderr 管道（16KB）写满时同样会卡死。
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(RipgrepTimeout);

        var hits = new List<VaultMatch>();
        var stdout = process.StandardOutput;
        try
        {
            while (await stdout.ReadLineAsync(limit.Token).ConfigureAwait(false) is { } line)
            {
                // 收够 take 条也不能停读：rg 还在往 stdout 写，管道（64KB）写满后它阻塞在 write 上、
                // 永远不退出，下面的 WaitForExitAsync 就一直等——工具调用永久挂起，整轮的会话 gate 不释放。
                // 所以这里只停止解析，继续把管道读到 EOF。
                if (hits.Count >= take)
                {
                    continue;
                }

                if (ParseMatchLine(line) is { } hit)
                {
                    hits.Add(new VaultMatch(
                        Path.GetRelativePath(vaultRoot, hit.Path),
                        hit.Line,
                        BuildSnippet(hit.Text, query)));
                }
            }

            await stderrTask.ConfigureAwait(false);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return hits; // rg 退出码 0=有命中 1=无命中 2=出错——统一按结果集处置
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new TimeoutException($"rg 检索超过 {RipgrepTimeout.TotalSeconds:0} 秒，改走托管扫描");
        }
        catch
        {
            Kill(process);
            throw;
        }
    }

    /// <summary>杀掉子进程（已退出 / 系统不允许时静默忽略）。</summary>
    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // 已经退出或无权杀：由调用方按失败处理
        }
    }

    /// <summary>解析 <c>rg --json</c> 的一条 match 行；非 match / 坏行返回 null。</summary>
    public static (string Path, int? Line, string Text)? ParseMatchLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.GetProperty("type").GetString() != "match")
            {
                return null;
            }

            var data = doc.RootElement.GetProperty("data");
            var path = data.GetProperty("path").GetProperty("text").GetString();
            var text = data.GetProperty("lines").GetProperty("text").GetString();
            var lineNumber = data.TryGetProperty("line_number", out var number) && number.TryGetInt32(out var value)
                ? value
                : (int?)null;
            return string.IsNullOrEmpty(path) || text is null ? null : (path, lineNumber, text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>片段 = 命中行围绕首次命中位置开窗（超长截断并补省略号）。</summary>
    public static string BuildSnippet(string line, string query)
    {
        var text = line.Trim();
        if (text.Length <= SnippetCap)
        {
            return text;
        }

        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, index <= 0 ? 0 : index - (SnippetCap / 2));
        var window = text.Substring(start, Math.Min(SnippetCap, text.Length - start));
        return (start > 0 ? "…" : string.Empty) + window + (start + SnippetCap < text.Length ? "…" : string.Empty);
    }

    /// <summary>托管扫描兜底（rg 不可用时）：md 先行、子串匹配、同样的片段开窗。</summary>
    public static IReadOnlyList<VaultMatch> ManagedSearch(string vaultRoot, string query, int take)
    {
        var hits = new List<VaultMatch>();
        var scanned = 0;
        foreach (var mdFirst in new[] { true, false })
        {
            foreach (var file in Directory.EnumerateFiles(vaultRoot, "*", SearchOption.AllDirectories))
            {
                if (hits.Count >= take || scanned >= ManagedScanFileLimit)
                {
                    return hits;
                }

                if (file.Contains($"{Path.DirectorySeparatorChar}.obsidian{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) != mdFirst)
                {
                    continue;
                }

                scanned++;
                var perFile = 0;
                var lineNumber = 0;
                foreach (var raw in File.ReadLines(file))
                {
                    lineNumber++;
                    if (raw.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new VaultMatch(Path.GetRelativePath(vaultRoot, file), lineNumber, BuildSnippet(raw, query)));
                        if (++perFile >= MaxMatchesPerFile || hits.Count >= take)
                        {
                            break;
                        }
                    }
                }
            }
        }

        return hits;
    }
}
