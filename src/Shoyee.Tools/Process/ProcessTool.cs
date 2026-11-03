using System.ComponentModel;
using Microsoft.Extensions.AI;
using Shoyee.Tools.Composition;

namespace Shoyee.Tools.Process;

/// <summary>
/// 后台进程工具族 <c>process_start</c> / <c>process_status</c> / <c>process_kill</c>（Hermes process 语义）：
/// 长命令后台跑、立刻拿 id、跑完由 <see cref="IProcessCompletionSink"/> 回执到当前对话。
/// <c>process_start</c> 挂审批（危险命令先问，规则同 terminal）。
/// </summary>
public static class ProcessTool
{
    /// <summary>起跑工具名（审批规则按名匹配）。</summary>
    public const string StartToolName = "process_start";

    /// <summary>状态工具名。</summary>
    public const string StatusToolName = "process_status";

    /// <summary>终止工具名。</summary>
    public const string KillToolName = "process_kill";

    /// <summary>构建 <c>process_*</c> 工具族。</summary>
    public static IReadOnlyList<ToolDescriptor> Create(ProcessRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var start = AIFunctionFactory.Create(
            async (
                [Description("要后台跑的 shell 命令。")] string command,
                [Description("备注：这命令是干嘛的（完成回执里带上）。")] string? note = null,
                [Description("超时秒数：到点自动杀掉整棵进程树并回执「超时被回收」（0 或省略 = 不限时）。")] int timeoutSeconds = 0,
                CancellationToken cancellationToken = default) =>
            {
                await Task.Yield();
                try
                {
                    var info = registry.Start(
                        command,
                        ProcessScope.SessionKey ?? "unknown",
                        note,
                        timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null);
                    return $"{info.Id} 已起跑（pid {info.Pid}），输出落 {info.LogPath}。"
                           + (timeoutSeconds > 0 ? $"超过 {timeoutSeconds}s 自动回收。" : string.Empty)
                           + $"跑完结果会自动回到这个对话；中途看进度用 {StatusToolName}，要停用 {KillToolName}。";
                }
                catch (Exception ex)
                {
                    return "起跑失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = StartToolName,
                Description = "后台跑长命令（几百秒的也行）：立刻返回进程 id；执行完自动把结果回执到当前对话，适合转码/批处理/长脚本这类不卡对话的活。输出全程落日志文件。可选 timeoutSeconds 到点自动回收（0=不限）。短命令用 terminal（60s 超时）。",
            });

        var status = AIFunctionFactory.Create(
            async (
                [Description("进程 id（process_start 返回的）。")] string id,
                [Description("看多少字符的输出尾部。")] int tailChars = 4000,
                CancellationToken cancellationToken = default) =>
            {
                await Task.Yield();
                var info = registry.Get(id);
                if (info is null)
                {
                    return $"没有 {id} 这个进程（列表：{string.Join(", ", registry.List().Select(static p => p.Id))}）。";
                }

                var running = info.Status == "running";
                var elapsed = (info.EndedAt ?? DateTimeOffset.Now) - info.StartedAt;
                var text = $"{info.Id} [{info.Status}] pid {info.Pid}，已跑 {elapsed:mm\\:ss}\n命令：{info.Command}";
                if (info.EndedAt is not null)
                {
                    text += $"\n退出码：{info.ExitCode}";
                }

                var tail = registry.ReadTail(id, tailChars);
                return text + (tail.Length == 0 ? string.Empty : $"\n输出尾部：\n{tail}")
                       + (running ? "\n（还在跑；跑完会自动回执到本对话）" : string.Empty);
            },
            new AIFunctionFactoryOptions
            {
                Name = StatusToolName,
                Description = "查后台进程状态与输出尾部（running/done/failed/killed/timeout）。",
            });

        var kill = AIFunctionFactory.Create(
            async ([Description("进程 id。")] string id, CancellationToken cancellationToken = default) =>
            {
                await Task.Yield();
                return registry.Kill(id)
                    ? $"{id} 已终止（整棵进程树）。"
                    : $"{id} 没有在跑（不存在或已结束）。";
            },
            new AIFunctionFactoryOptions
            {
                Name = KillToolName,
                Description = "终止一个后台进程（含它拉起的子进程）。",
            });

        return
        [
            ToolDescriptor.Create(start, ToolScope.Work, writes: true, requiresApproval: true),
            ToolDescriptor.Create(status, ToolScope.Work, writes: false, requiresApproval: false),
            ToolDescriptor.Create(kill, ToolScope.Work, writes: true, requiresApproval: false),
        ];
    }
}
