namespace Shoyee.Tools.Process;

/// <summary>后台进程信息（Hermes process 语义）。</summary>
/// <param name="Id">进程 id（<c>p-xxxxxx</c>）。</param>
/// <param name="Command">执行的命令。</param>
/// <param name="Pid">系统 pid（起跑后填）。</param>
/// <param name="Status">running / done / failed / killed / timeout。</param>
/// <param name="StartedAt">起跑时间。</param>
/// <param name="EndedAt">结束时间（未结束为 null）。</param>
/// <param name="ExitCode">退出码（未结束为 null）。</param>
/// <param name="LogPath">输出日志路径。</param>
public sealed record ProcessInfo(
    string Id,
    string Command,
    int? Pid,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int? ExitCode,
    string LogPath);
