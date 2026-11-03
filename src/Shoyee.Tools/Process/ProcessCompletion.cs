namespace Shoyee.Tools.Process;

/// <summary>后台进程完成回执（交给渠道/会话管线，像用户消息一样推进原对话）。</summary>
/// <param name="ProcessId">进程 id。</param>
/// <param name="SessionKey">起跑时所在会话键。</param>
/// <param name="Command">执行的命令。</param>
/// <param name="Note">起跑备注。</param>
/// <param name="Status">终态（done / failed / killed / timeout）。</param>
/// <param name="ExitCode">退出码。</param>
/// <param name="OutputTail">输出尾部（截断）。</param>
public sealed record ProcessCompletion(
    string ProcessId,
    string SessionKey,
    string Command,
    string? Note,
    string Status,
    int? ExitCode,
    string OutputTail);
