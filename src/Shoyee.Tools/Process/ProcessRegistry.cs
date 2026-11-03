using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shoyee.Tools.Process;

/// <summary>
/// 后台进程表（Hermes process 语义）：长命令起跑立刻拿 id，跑完把「完成 + 输出尾部」交给
/// <see cref="IProcessCompletionSink"/> 推进原会话；输出全程落 <c>{日志根}/{id}.log</c>。
/// 进程随宿主生死（重启不恢复），日志留档。
/// </summary>
public sealed class ProcessRegistry : IDisposable
{
    /// <summary>并发上限（防一口气铺满机器）。</summary>
    public const int MaxConcurrent = 8;

    /// <summary>完成回执里的输出尾部截断。</summary>
    private const int NotifyTailChars = 3000;

    private readonly string _logRoot;
    private volatile string _defaultWorkingDirectory;
    private volatile string[] _denyPatterns;
    private readonly IProcessCompletionSink? _sink;
    private readonly ILogger<ProcessRegistry> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>创建进程表。</summary>
    /// <param name="logRoot">日志目录（<c>{工作区}/.processes</c>）。</param>
    /// <param name="defaultWorkingDirectory">默认工作目录。</param>
    /// <param name="denyPatterns">硬禁命令正则（命中直接拒，不进审批）。</param>
    /// <param name="sink">完成回执投递口（可空）。</param>
    /// <param name="logger">日志。</param>
    public ProcessRegistry(
        string logRoot,
        string defaultWorkingDirectory,
        IEnumerable<string>? denyPatterns = null,
        IProcessCompletionSink? sink = null,
        ILogger<ProcessRegistry>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultWorkingDirectory);
        _logRoot = logRoot;
        _defaultWorkingDirectory = defaultWorkingDirectory;
        _denyPatterns = (denyPatterns ?? []).ToArray();
        _sink = sink;
        _logger = logger ?? NullLogger<ProcessRegistry>.Instance;
    }

    /// <summary>热更新默认工作目录与硬禁规则（<c>config.reload</c> 的 shell 段）。</summary>
    /// <param name="defaultWorkingDirectory">新的默认工作目录。</param>
    /// <param name="denyPatterns">新的硬禁正则。</param>
    public void Reload(string defaultWorkingDirectory, IEnumerable<string>? denyPatterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultWorkingDirectory);
        _defaultWorkingDirectory = defaultWorkingDirectory;
        _denyPatterns = (denyPatterns ?? []).ToArray();
    }

    /// <summary>起跑一个后台进程，立刻返回信息（状态 running）。</summary>
    /// <param name="command">要跑的命令。</param>
    /// <param name="sessionKey">回执归属的会话键。</param>
    /// <param name="note">备注（回执带上）。</param>
    /// <param name="timeout">可选超时；到点杀整棵进程树并回执「超时被回收」。</param>
    public ProcessInfo Start(string command, string sessionKey, string? note = null, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("命令不能为空。", nameof(command));
        }

        foreach (var pattern in _denyPatterns)
        {
            if (Regex.IsMatch(command, pattern))
            {
                throw new InvalidOperationException($"命令命中硬禁规则（{pattern}），拒不执行。");
            }
        }

        lock (_gate)
        {
            if (_entries.Values.Count(static e => e.Info.Status == "running") >= MaxConcurrent)
            {
                throw new InvalidOperationException($"后台进程已满（{MaxConcurrent} 个），先 process_kill 清理。");
            }

            var id = "p-" + Guid.NewGuid().ToString("N")[..6];
            Directory.CreateDirectory(_logRoot);
            var logPath = Path.Combine(_logRoot, id + ".log");
            var entry = new Entry(new ProcessInfo(id, command, null, "running", DateTimeOffset.Now, null, null, logPath), sessionKey, note, timeout);
            _entries[id] = entry;

            try
            {
                Launch(entry);
            }
            catch (Exception ex)
            {
                _entries.Remove(id);
                throw new InvalidOperationException("起跑失败：" + ex.Message, ex);
            }

            return entry.Info;
        }
    }

    /// <summary>全部进程快照。</summary>
    public IReadOnlyList<ProcessInfo> List()
    {
        lock (_gate)
        {
            return _entries.Values.Select(static e => e.Info).ToList();
        }
    }

    /// <summary>取单个进程信息（没有返回 null）。</summary>
    public ProcessInfo? Get(string id)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(id, out var entry) ? entry.Info : null;
        }
    }

    /// <summary>读输出尾部（日志文件末尾）。</summary>
    public string ReadTail(string id, int maxChars = 4000)
    {
        var info = Get(id);
        if (info is null || !File.Exists(info.LogPath))
        {
            return string.Empty;
        }

        var text = File.ReadAllText(info.LogPath, Encoding.UTF8);
        return text.Length <= maxChars ? text : "…" + text[^maxChars..];
    }

    /// <summary>终止进程（整棵进程树）；返回是否真的终止了在跑的进程。</summary>
    public bool Kill(string id)
    {
        Entry? entry;
        lock (_gate)
        {
            entry = _entries.TryGetValue(id, out var found) ? found : null;
        }

        if (entry?.Process is null || entry.Info.Status != "running")
        {
            return false;
        }

        entry.Killed = true;
        try
        {
            entry.Process.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values.Where(static e => e.Info.Status == "running"))
            {
                entry.Killed = true;
                try
                {
                    entry.Process?.Kill(entireProcessTree: true);
                }
                catch
                {
                    // 清理尽力而为
                }
            }
        }
    }

    private void Launch(Entry entry)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = _defaultWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(entry.Info.Command);

        var process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var writer = new StreamWriter(entry.Info.LogPath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (writer) { writer.WriteLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (writer) { writer.WriteLine(e.Data); } } };
        process.Exited += (_, _) =>
        {
            try
            {
                process.WaitForExit(); // 等重定向输出排干
            }
            catch
            {
                // 忽略
            }

            lock (writer)
            {
                writer.Dispose();
            }

            entry.Finish(process.ExitCode, process.Id);
            process.Dispose();
            OnFinished(entry);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        entry.SetProcess(process);

        if (entry.Timeout is { } timeout)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(timeout).ConfigureAwait(false);
                if (entry.Info.Status == "running" && !entry.Killed)
                {
                    entry.Killed = true;
                    entry.TimedOut = true;
                    try
                    {
                        entry.Process?.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // 已经退了就罢
                    }
                }
            });
        }
    }

    private void OnFinished(Entry entry)
    {
        if (_sink is null)
        {
            return;
        }

        var info = entry.Info;
        var completion = new ProcessCompletion(
            info.Id,
            entry.SessionKey,
            info.Command,
            entry.Note,
            info.Status,
            info.ExitCode,
            ReadTail(info.Id, NotifyTailChars));

        _ = Task.Run(async () =>
        {
            try
            {
                await _sink.NotifyAsync(completion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 回执失败不掀桌（进程已完成，日志留档）
                _logger.LogWarning(ex, "后台进程完成回执投递失败：{ProcessId}", info.Id);
            }
        });
    }

    private sealed class Entry(ProcessInfo info, string sessionKey, string? note, TimeSpan? timeout = null)
    {
        public ProcessInfo Info { get; private set; } = info;

        public string SessionKey { get; } = sessionKey;

        public string? Note { get; } = note;

        public TimeSpan? Timeout { get; } = timeout;

        public System.Diagnostics.Process? Process { get; private set; }

        public bool Killed { get; set; }

        public bool TimedOut { get; set; }

        public void SetProcess(System.Diagnostics.Process process)
        {
            Process = process;
            Info = Info with { Pid = process.Id };
        }

        public void Finish(int exitCode, int pid)
        {
            var status = Killed ? (TimedOut ? "timeout" : "killed") : exitCode == 0 ? "done" : "failed";
            Info = Info with { Pid = pid, Status = status, EndedAt = DateTimeOffset.Now, ExitCode = exitCode };
        }
    }
}
