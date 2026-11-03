using System.Text.Json;
using Microsoft.Agents.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Files;
using Shoyee.Tools.Process;
using Shoyee.Tools.Shell;

namespace Shoyee.Tools.Approval;

/// <summary>
/// 工具审批策略（接框架 <see cref="ToolApprovalAgentOptions.AutoApprovalRules"/>）：
/// **工作区 + 本地笔记库根内的文件写入免审批**；白名单外写入、危险命令一律浮出待审批。
/// 只读 <c>file_access_*</c> 不挂审批；索引写库不挂审批；MCP 保持 Secretary 的做法（不包装，交给服务端自己把关）。
/// </summary>
public static class ToolApprovalRules
{
    /// <summary><c>file_access_*</c> 工具族前缀。</summary>
    private const string FileAccessPrefix = "file_access_";

    /// <summary>路径参数名（文件工具统一用 <c>path</c>）。</summary>
    private static readonly string[] PathArguments = ["path"];

    /// <summary>
    /// 白名单规则：<c>file_access_*</c> 且路径落在工作区 / 笔记库根内 → 自动放行；其余返回 false（走 HITL）。
    /// </summary>
    /// <param name="config">工作区配置。</param>
    /// <param name="notesRoot">本地笔记库根（未配置 / 远端 provider 传 null）。</param>
    public static Func<ToolAutoApprovalRuleContext, ValueTask<bool>> WorkspacePathsAutoApprovalRule(
        WorkspaceConfig config,
        string? notesRoot = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var roots = new[] { config.Root, notesRoot }
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Select(static r => RealPath.Resolve(r!)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar)
            .ToList();
        var defaultDirectory = FileAccessTools.DefaultDirectory(config);

        return context =>
        {
            var call = context.FunctionCallContent;
            if (!call.Name.StartsWith(FileAccessPrefix, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            var path = GetArgument(call.Arguments, PathArguments);
            if (string.IsNullOrWhiteSpace(path))
            {
                return ValueTask.FromResult(false);
            }

            var fullPath = FileAccessTools.Resolve(path, defaultDirectory);
            // 按真实路径比较：工作区内的符号链接不能指向外部还享受免审批
            var candidate = RealPath.Resolve(fullPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return ValueTask.FromResult(roots.Any(root => candidate.StartsWith(root, StringComparison.Ordinal)));
        };
    }

    /// <summary>
    /// 受限终端规则：<c>terminal</c> / <c>process_start</c> 且命令不危险 → 自动放行；危险命令返回 false → 浮出审批。
    /// </summary>
    public static Func<ToolAutoApprovalRuleContext, ValueTask<bool>> SafeShellAutoApprovalRule { get; }
        = context =>
        {
            var call = context.FunctionCallContent;
            if (!string.Equals(call.Name, ShellTool.ToolName, StringComparison.Ordinal)
                && !string.Equals(call.Name, ProcessTool.StartToolName, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            var command = GetArgument(call.Arguments, ["command"]);
            return ValueTask.FromResult(!ShellSafety.IsDangerous(command));
        };

    /// <summary>按候选参数名取字符串参数（兼容 JsonElement / string）。</summary>
    public static string? GetArgument(IDictionary<string, object?>? arguments, IReadOnlyList<string> names)
    {
        if (arguments is null)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (!arguments.TryGetValue(name, out var raw) || raw is null)
            {
                continue;
            }

            return raw switch
            {
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                string text => text,
                _ => raw.ToString(),
            };
        }

        return null;
    }
}
