using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Composition;

namespace Shoyee.Tools.Files;

/// <summary>
/// <c>file_access_*</c> 八件套（读、按行读、列目录、内容搜索、写、删、精确替换、按行替换）。
///
/// 路径约定：绝对路径直接用；相对路径按<b>默认目录</b>（<c>shell.workingDirectory</c>，缺省
/// <c>{工作区}/Workspace</c>）解析；回执一律给绝对路径。读写走 System.IO（不用框架 store，
/// 避免 BOM 与巨型文件整读的问题）。
///
/// 只读工具永不挂审批；写工具挂审批，工作区 + 本地笔记库内由
/// <see cref="Approval.ToolApprovalRules"/> 自动放行，之外浮出审批。
/// </summary>
public static class FileAccessTools
{
    /// <summary>读文件工具名。</summary>
    public const string ReadToolName = "file_access_read";

    /// <summary>按行读工具名。</summary>
    public const string ReadLinesToolName = "file_access_read_lines";

    /// <summary>列目录工具名。</summary>
    public const string LsToolName = "file_access_ls";

    /// <summary>内容搜索工具名。</summary>
    public const string GrepToolName = "file_access_grep";

    /// <summary>写文件工具名。</summary>
    public const string WriteToolName = "file_access_write";

    /// <summary>删文件工具名。</summary>
    public const string DeleteToolName = "file_access_delete";

    /// <summary>精确替换工具名。</summary>
    public const string ReplaceToolName = "file_access_replace";

    /// <summary>整行替换工具名。</summary>
    public const string ReplaceLinesToolName = "file_access_replace_lines";

    /// <summary><c>file_access_read</c> 超过此字节数直接拒读。</summary>
    private const long MaxReadBytes = 2 * 1024 * 1024;

    /// <summary><c>file_access_read</c> 回执字符上限。</summary>
    private const int MaxReadChars = 200_000;

    /// <summary><c>ls</c> / <c>grep</c> 回执条目上限。</summary>
    private const int MaxListedEntries = 300;

    /// <summary>grep 单文件上限（超过跳过，不把巨型文件读进内存）。</summary>
    private const long MaxGrepFileBytes = 10 * 1024 * 1024;

    /// <summary>工具级路径说明（动态默认目录放在 Description 里，特性实参只能是常量）。</summary>
    private const string PathHint = "文件/目录路径：推荐绝对路径；相对路径按默认工作目录解析（各工具说明里有真实路径）。";

    /// <summary>构建八个文件工具。</summary>
    /// <param name="config">工作区配置（默认目录与说明用）。</param>
    /// <param name="confineWrites">
    /// true = 写/删/替换只允许工作区 + 本地笔记库内（RP 用；普通 ChatClientAgent 没有审批中间件，
    /// 不能像工作模式那样靠审批兜住外部路径）。读不受影响。
    /// </param>
    /// <param name="notesRoot">本地笔记库根（未配置 / 远端 provider 传 null）。</param>
    /// <param name="pathPolicy">共享路径策略（<c>config.reload</c> 后可原地更新；null = 按配置新建）。</param>
    public static IReadOnlyList<ToolDescriptor> Create(
        WorkspaceConfig config,
        bool confineWrites = false,
        string? notesRoot = null,
        ToolPathPolicy? pathPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var defaultDirectory = DefaultDirectory(config);
        pathPolicy ??= ToolPathPolicy.FromConfig(config);
        var writableRoots = new[] { config.Root, notesRoot }
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => root!)
            .ToArray();
        var notesNote = string.IsNullOrWhiteSpace(notesRoot) ? string.Empty : "和笔记库内写入免审批";
        var writeLock = new SemaphoreSlim(1, 1);
        var pathNote = $"（相对路径按 {defaultDirectory} 解析）";

        var read = AIFunctionFactory.Create(
            async ([Description(PathHint)] string path, CancellationToken cancellationToken = default) =>
            {
                var abs = Resolve(path, defaultDirectory);
                if (pathPolicy.Check(abs) is { } denied)
                {
                    return denied;
                }

                try
                {
                    var info = new FileInfo(abs);
                    if (info.Exists && info.Length > MaxReadBytes)
                    {
                        return $"文件太大（{info.Length / 1024} KB），不整读：{abs}。"
                               + "要哪段用 file_access_read_lines 按行读，找内容用 file_access_grep。";
                    }

                    if (!File.Exists(abs))
                    {
                        return $"文件不存在：{abs}（不确定就先 file_access_ls 看目录，别凭印象猜）";
                    }

                    var content = await File.ReadAllTextAsync(abs, cancellationToken).ConfigureAwait(false);
                    if (content.Length > MaxReadChars)
                    {
                        int cut = content.LastIndexOf('\n', MaxReadChars - 1) + 1;
                        if (cut <= 0)
                        {
                            cut = MaxReadChars;
                        }

                        return content[..cut]
                               + $"\n…（文件较长已截断：给了前 {cut} 字符，全文 {content.Length}；"
                               + "后面的部分用 file_access_read_lines 按行读）";
                    }

                    return content;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "读失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = ReadToolName,
                Description = "读整个文件的文本内容。大文件（>2MB）会拒读并提示改用 read_lines / grep；较长的会截断并说明截断位置。" + pathNote,
            });

        var readLines = AIFunctionFactory.Create(
            async (
                [Description(PathHint)] string path,
                [Description("起始行号（1-based，含）。")] int startLine,
                [Description("结束行号（1-based，含）；省略 = 读到文件尾，越界自动截到最后一行。")] int? endLine = null,
                CancellationToken cancellationToken = default) =>
            {
                var abs = Resolve(path, defaultDirectory);
                if (pathPolicy.Check(abs) is { } denied)
                {
                    return denied;
                }

                try
                {
                    if (!File.Exists(abs))
                    {
                        return $"文件不存在：{abs}";
                    }

                    var content = await File.ReadAllTextAsync(abs, cancellationToken).ConfigureAwait(false);
                    var lines = FileEditor.SliceLines(content, startLine, endLine);
                    var builder = new StringBuilder();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        builder.Append(startLine + i).Append('\t').Append(lines[i]);
                    }

                    return builder.ToString();
                }
                catch (ArgumentException ex)
                {
                    return ex.Message;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "读失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = ReadLinesToolName,
                Description = "按行号读文件片段（1-based 闭区间）。每行输出「行号\\t内容」，行号可直接用于 replace_lines；改小块前先用它拿准确行号。" + pathNote,
            });

        var ls = AIFunctionFactory.Create(
            async (
                [Description("要列的目录路径（省略 = 默认工作目录）。")] string? directory = null,
                [Description("按名字过滤的 glob（如 \"*.md\"），只匹配条目名。省略 = 全列。")] string? globPattern = null,
                CancellationToken cancellationToken = default) =>
            {
                await Task.Yield();
                var abs = Resolve(string.IsNullOrWhiteSpace(directory) ? defaultDirectory : directory!, defaultDirectory);
                if (pathPolicy.Check(abs) is { } denied)
                {
                    return denied;
                }

                try
                {
                    if (abs == Path.GetPathRoot(abs))
                    {
                        return "别列文件系统根，范围太大；给个具体目录。";
                    }

                    if (!Directory.Exists(abs))
                    {
                        return $"目录不存在：{abs}";
                    }

                    var matcher = CompileGlob(globPattern);
                    var entries = new DirectoryInfo(abs).EnumerateFileSystemInfos()
                        .Where(e => matcher is null || matcher.IsMatch(e.Name))
                        .Where(e => !pathPolicy.IsDenied(e.FullName))
                        .OrderByDescending(static e => e is DirectoryInfo)
                        .ThenBy(static e => e.Name, StringComparer.Ordinal)
                        .ToList();

                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries.Count == 0)
                    {
                        return $"（空）{abs}：没有匹配的条目。";
                    }

                    var builder = new StringBuilder();
                    builder.Append("目录 ").Append(abs).AppendLine();
                    foreach (var entry in entries.Take(MaxListedEntries))
                    {
                        builder.Append(entry is DirectoryInfo ? "d" : "f")
                            .Append('\t')
                            .AppendLine(entry.FullName);
                    }

                    if (entries.Count > MaxListedEntries)
                    {
                        builder.Append($"…（共 {entries.Count} 项，只列了前 {MaxListedEntries}；加 globPattern 过滤）");
                    }

                    return builder.ToString();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "列目录失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = LsToolName,
                Description = $"列目录的直接子项（不递归），返回「d/f + 绝对路径」（目录在前）。不传 directory = {defaultDirectory}。",
            });

        var grep = AIFunctionFactory.Create(
            async (
                [Description("正则表达式（大小写不敏感）。")] string regexPattern,
                [Description("限定文件的 glob（如 \"**/*.md\"，* 不跨目录、** 跨）。省略 = 全部文件。")] string? globPattern = null,
                [Description("搜索起始目录（递归；省略 = 默认工作目录）。")] string? directory = null,
                CancellationToken cancellationToken = default) =>
            {
                await Task.Yield();
                var abs = Resolve(string.IsNullOrWhiteSpace(directory) ? defaultDirectory : directory!, defaultDirectory);
                if (pathPolicy.Check(abs) is { } denied)
                {
                    return denied;
                }

                try
                {
                    if (!Directory.Exists(abs))
                    {
                        return $"目录不存在：{abs}";
                    }

                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
                    var glob = CompileGlob(globPattern);
                    var rows = new List<string>();
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                        IgnoreInaccessible = true,
                    };

                    foreach (var file in Directory.EnumerateFiles(abs, "*", options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (pathPolicy.IsDenied(file))
                        {
                            continue;
                        }

                        var relative = Path.GetRelativePath(abs, file).Replace('\\', '/');
                        if (glob is not null && !glob.IsMatch(relative))
                        {
                            continue;
                        }

                        var info = new FileInfo(file);
                        if (!info.Exists || info.Length > MaxGrepFileBytes || IsBinary(file))
                        {
                            continue;
                        }

                        string[] lines;
                        try
                        {
                            lines = File.ReadAllLines(file);
                        }
                        catch
                        {
                            continue;
                        }

                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (regex.IsMatch(lines[i]))
                            {
                                var text = lines[i].Trim();
                                rows.Add($"{file}:{i + 1}: {(text.Length > 160 ? text[..160] + "…" : text)}");
                                if (rows.Count >= MaxListedEntries)
                                {
                                    break;
                                }
                            }
                        }

                        if (rows.Count >= MaxListedEntries)
                        {
                            break;
                        }
                    }

                    if (rows.Count == 0)
                    {
                        return $"没搜到：{abs} 下没有匹配「{regexPattern}」的内容（换关键词、去掉 globPattern 或扩大 directory 再试）。";
                    }

                    var builder = new StringBuilder();
                    foreach (var row in rows)
                    {
                        builder.AppendLine(row);
                    }

                    if (rows.Count >= MaxListedEntries)
                    {
                        builder.Append($"…（最多只列 {MaxListedEntries} 处；缩小 directory 或加 globPattern）");
                    }

                    return builder.ToString();
                }
                catch (ArgumentException ex)
                {
                    return "正则不合法：" + ex.Message;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "搜索失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = GrepToolName,
                Description = $"按正则递归搜文件内容，返回「绝对路径:行号: 行内容」，行号可直接用于 replace_lines。不传 directory = 搜 {defaultDirectory}。",
            });

        var write = AIFunctionFactory.Create(
            async (
                [Description(PathHint)] string path,
                [Description("文件全文内容。")] string content,
                [Description("已存在时是否覆盖（默认 false = 拒绝覆盖）。")] bool overwrite = false,
                CancellationToken cancellationToken = default) =>
            {
                var abs = Resolve(path, defaultDirectory);
                if (pathPolicy.Check(abs) is { } denied)
                {
                    return denied;
                }

                if (WriteGuard(abs, writableRoots, confineWrites) is { } blocked)
                {
                    return blocked;
                }

                await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!overwrite && File.Exists(abs))
                    {
                        return $"已存在，没覆盖：{abs}。小改动用 file_access_replace / replace_lines；确认覆盖就加 overwrite=true。";
                    }

                    var parent = Path.GetDirectoryName(abs);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    await File.WriteAllTextAsync(abs, content, new UTF8Encoding(false), cancellationToken)
                        .ConfigureAwait(false);
                    return "已写入：" + abs;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "写失败：" + ex.Message;
                }
                finally
                {
                    writeLock.Release();
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = WriteToolName,
                Description = $"写文件（父目录自动创建，UTF-8 无 BOM）。默认不覆盖已存在文件（overwrite=false）；小改动优先 replace / replace_lines。工作区（{config.Root}）{notesNote}写入免审批，其他位置会先申请审批。" + pathNote,
            });

        var delete = AIFunctionFactory.Create(
            async ([Description(PathHint)] string path, CancellationToken cancellationToken = default) =>
            {
                await Task.Yield();
                var abs = Resolve(path, defaultDirectory);
                if (pathPolicy.Check(abs) is { } denied)
                {
                    return denied;
                }

                if (WriteGuard(abs, writableRoots, confineWrites) is { } blocked)
                {
                    return blocked;
                }

                await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!File.Exists(abs))
                    {
                        return $"文件不存在：{abs}";
                    }

                    File.Delete(abs);
                    return "已删除：" + abs;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "删失败：" + ex.Message;
                }
                finally
                {
                    writeLock.Release();
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = DeleteToolName,
                Description = "删文件（不支持删目录）。老板没明说别删、别覆盖已有文件。" + pathNote,
            });

        var replace = AIFunctionFactory.Create(
            async (
                [Description(PathHint)] string path,
                [Description("要找的原文本（唯一命中才安全；不唯一会报错）。")] string oldString,
                [Description("替换成的新文本。")] string newString,
                [Description("true = 全部替换（oldString 多处命中时才用）。")] bool replaceAll = false,
                CancellationToken cancellationToken = default) =>
            {
                var abs = Resolve(path, defaultDirectory);
                if (pathPolicy.Check(abs) is { } denied)
                {
                    return denied;
                }

                if (WriteGuard(abs, writableRoots, confineWrites) is { } blocked)
                {
                    return blocked;
                }

                await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!File.Exists(abs))
                    {
                        return $"文件不存在：{abs}";
                    }

                    var content = await File.ReadAllTextAsync(abs, cancellationToken).ConfigureAwait(false);
                    var (newContent, count) = FileEditor.ApplyReplace(content, oldString, newString, replaceAll);
                    await File.WriteAllTextAsync(abs, newContent, new UTF8Encoding(false), cancellationToken)
                        .ConfigureAwait(false);
                    return $"替换了 {count} 处：{abs}";
                }
                catch (ArgumentException ex)
                {
                    return ex.Message;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "替换失败：" + ex.Message;
                }
                finally
                {
                    writeLock.Release();
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = ReplaceToolName,
                Description = "精确文本替换（oldString 找不到或不唯一会报错，可操作）。小改动优先用它，别整文件重写。" + pathNote,
            });

        var replaceLines = AIFunctionFactory.Create(
            async (
                [Description(PathHint)] string path,
                [Description("行编辑列表：line_number（1-based）/ new_line（整行新文本，自带换行；空串=删该行）/ expected_line（可选，当前行文本，防行号过期写错行）。")] List<FileLineEdit> edits,
                CancellationToken cancellationToken = default) =>
            {
                var abs = Resolve(path, defaultDirectory);
                if (pathPolicy.Check(abs) is { } denied)
                {
                    return denied;
                }

                if (WriteGuard(abs, writableRoots, confineWrites) is { } blocked)
                {
                    return blocked;
                }

                await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!File.Exists(abs))
                    {
                        return $"文件不存在：{abs}";
                    }

                    var content = await File.ReadAllTextAsync(abs, cancellationToken).ConfigureAwait(false);
                    var newContent = FileEditor.ApplyReplaceLines(content, edits);
                    await File.WriteAllTextAsync(abs, newContent, new UTF8Encoding(false), cancellationToken)
                        .ConfigureAwait(false);
                    return $"改了 {edits.Count} 行：{abs}";
                }
                catch (ArgumentException ex)
                {
                    return ex.Message;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "改行失败：" + ex.Message;
                }
                finally
                {
                    writeLock.Release();
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = ReplaceLinesToolName,
                Description = "按行号整行替换（可一次多行；new_line 空串 = 删该行）。行号从 read_lines / grep 来；建议带 expected_line 防行号过期。" + pathNote,
            });

        return
        [
            ToolDescriptor.Create(read, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(readLines, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(ls, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(grep, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(write, ToolScope.All, writes: true, requiresApproval: true),
            ToolDescriptor.Create(delete, ToolScope.All, writes: true, requiresApproval: true),
            ToolDescriptor.Create(replace, ToolScope.All, writes: true, requiresApproval: true),
            ToolDescriptor.Create(replaceLines, ToolScope.All, writes: true, requiresApproval: true),
        ];
    }

    /// <summary>默认工作目录（与 terminal 的 cwd 同一处）。</summary>
    /// <param name="config">工作区配置。</param>
    public static string DefaultDirectory(WorkspaceConfig config)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(config.Shell?.WorkingDirectory)
            ? Path.Combine(config.Root, "Workspace")
            : config.Shell.WorkingDirectory);

    /// <summary>路径解析：<c>~</c> 展开、绝对路径归一、相对路径拼到默认目录。</summary>
    public static string Resolve(string path, string defaultDirectory)
    {
        var trimmed = (path ?? string.Empty).Trim();
        if (trimmed.StartsWith('~'))
        {
            trimmed = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                trimmed.TrimStart('~').TrimStart('/', '\\'));
        }

        return Path.GetFullPath(
            Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(defaultDirectory, trimmed));
    }

    /// <summary>glob → 正则（<c>*</c> 不跨目录、<c>**</c> 跨、<c>?</c> 单字符）。</summary>
    public static Regex? CompileGlob(string? globPattern)
    {
        if (string.IsNullOrWhiteSpace(globPattern))
        {
            return null;
        }

        var escaped = Regex.Escape(globPattern)
            .Replace(@"\*\*", "\u0001", StringComparison.Ordinal)
            .Replace(@"\*", @"[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", @"[^/]", StringComparison.Ordinal)
            .Replace("\u0001", ".*", StringComparison.Ordinal);
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    /// <summary>写路径围栏：RP 没有审批，只能在真实路径落于工作区 / 笔记库内时写。</summary>
    private static string? WriteGuard(string absolutePath, IReadOnlyList<string> writableRoots, bool confineWrites)
    {
        if (!confineWrites)
        {
            return null;
        }

        var target = RealPath.Resolve(absolutePath);
        foreach (var writableRoot in writableRoots)
        {
            var root = RealPath.Resolve(writableRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (target.StartsWith(root, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return "越界：RP 只能写工作区 / 笔记库内的文件。";
    }

    private static bool IsBinary(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var buffer = new byte[8192];
            var read = stream.Read(buffer, 0, buffer.Length);
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }
}
