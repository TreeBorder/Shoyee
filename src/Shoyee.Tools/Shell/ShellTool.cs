using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;

namespace Shoyee.Tools.Shell;

/// <summary>
/// 受限终端工具 <c>terminal</c>：直接使用框架 <c>LocalShellExecutor</c>（持久 shell、超时、输出上限、
/// 工作目录限定、deny 策略），执行体不自写。危险命令由
/// <see cref="Approval.ToolApprovalRules.SafeShellAutoApprovalRule"/> 浮出审批。
///
/// 用 <see cref="ShellMode.Stateless"/>：工具面是 daemon 级单例，而框架明确要求 persistent 执行器
/// 每个会话一个（否则 cwd/环境变量/后台任务会在会话间泄漏）。无状态模式下每条命令新起 shell，
/// 工作目录由 <c>WorkingDirectory</c> 固定，足够本项目的用法。
/// </summary>
public static class ShellTool
{
    /// <summary>工具名（审批规则按名匹配）。</summary>
    public const string ToolName = "terminal";

    /// <summary>构建 <c>terminal</c>。</summary>
    public static AIFunction Create(WorkspaceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var shell = config.Shell ?? new ShellOptions();
        var workingDirectory = WorkingDirectoryOf(config);

        var executor = new LocalShellExecutor(new LocalShellExecutorOptions
        {
            Mode = ShellMode.Stateless,
            WorkingDirectory = workingDirectory,
            ConfineWorkingDirectory = true,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, shell.TimeoutSeconds)),
            MaxOutputBytes = Math.Max(1024, shell.MaxOutputBytes),
            Policy = new ShellPolicy(denyList: shell.DenyPatterns),
            Shell = ShellIsolation.TryPrepare(config.Root),
            // 审批不在这里包：ToolsetBuilder 统一按元数据挂 ApprovalRequiredAIFunction，
            // 这里再包一层会出现双重审批包装。
            AcknowledgeUnsafe = true,
        });

        return executor.AsAIFunction(
            name: ToolName,
            description: $"受限终端：本地执行 shell 命令（有超时与输出上限）。当前工作目录 = {workingDirectory}（相对路径都从这里算）。危险命令（rm/sudo/kill/系统管理等）会先申请审批。命令必须非交互：不要跑 vim/top/ssh 或等待 stdin 的子命令，会卡到超时。",
            requireApproval: false);
    }

    /// <summary>实际工作目录（<c>shell.workingDirectory</c>，缺省 = <c>{工作区}/Workspace</c>）。</summary>
    public static string WorkingDirectoryOf(WorkspaceConfig config)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(config.Shell?.WorkingDirectory)
            ? Path.Combine(config.Root, "Workspace")
            : config.Shell.WorkingDirectory);
}
