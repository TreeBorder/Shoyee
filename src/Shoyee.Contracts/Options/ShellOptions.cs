namespace Shoyee.Contracts.Options;

/// <summary>受限终端配置（可空 = 用默认值）。</summary>
/// <param name="WorkingDirectory">工作目录；空 = <c>{工作区}/Workspace</c>（文件工具的默认目录同一处）。</param>
/// <param name="TimeoutSeconds">单条命令超时秒数。</param>
/// <param name="MaxOutputBytes">输出上限（字节）。</param>
/// <param name="DenyPatterns">硬禁命令正则（命中即拒，不进审批）。</param>
public sealed record ShellOptions(
    string? WorkingDirectory = null,
    int TimeoutSeconds = 60,
    int MaxOutputBytes = 200_000,
    IReadOnlyList<string>? DenyPatterns = null);
