using Shoyee.Contracts.Domain;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// <c>/cron [list]</c>：只读列出当前模式下的定时任务（含已停用的）。
///
/// 只读是刻意的：新增/启停/删除仍归她的 <c>cron_*</c> 工具与桌面定时任务页，
/// IM 再开一条写路径就成第三份真源。任务连败会被自动停用，所以默认把停用项也列出来。
/// </summary>
public sealed class CronCommand : ICommandHandler
{
    private readonly ICronRepository _jobs;

    /// <summary>创建指令。</summary>
    /// <param name="jobs">定时任务仓库。</param>
    public CronCommand(ICronRepository jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        _jobs = jobs;
    }

    /// <inheritdoc />
    public string CommandName => "cron";

    /// <inheritdoc />
    public string Description => "定时任务";

    /// <inheritdoc />
    public async Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var argument = context.Arguments.Trim();
        if (argument.Length > 0 && !argument.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return "⌨️ 用法：/cron 或 /cron list（只读；增删改找她说或去桌面定时任务页）";
        }

        var all = await _jobs.ListAsync(enabledOnly: false, cancellationToken).ConfigureAwait(false);
        var mine = all
            .Where(job => job.Mode == context.Mode)
            .OrderBy(static job => job.Name, StringComparer.Ordinal)
            .ToList();
        if (mine.Count == 0)
        {
            return "📭 还没有定时任务。";
        }

        var rows = mine
            .Select(static job => new[]
            {
                TableText.Cell(job.Name),
                TableText.Cell(job.Schedule),
                TableText.Time(job.NextRunAt),
                Status(job),
            })
            .ToList();

        var text = $"📅 定时任务（{mine.Count} 个）\n"
                   + TableText.Table(["任务", "调度", "下次", "状态"], rows)
                   + "\n🔎 详情：" + string.Join(" · ", mine.Select(static job => job.Id));

        var failed = mine.FirstOrDefault(static job => !string.IsNullOrWhiteSpace(job.LastError));
        if (failed is not null)
        {
            text += $"\n⚠️ {TableText.Cell(failed.Name)}：{TableText.Cell(failed.LastError)}";
        }

        return text;
    }

    private static string Status(CronJobRecord job)
        => !job.Enabled
            ? "⏸ 已停用"
            : job.LastStatus switch
            {
                "failed" => "⚠️ 上次失败",
                "succeeded" => "✅ 正常",
                _ => "▶️ 未运行",
            };
}
