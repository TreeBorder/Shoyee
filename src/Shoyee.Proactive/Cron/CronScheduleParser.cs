using System.Globalization;
using Quartz;

namespace Shoyee.Proactive.Cron;

/// <summary>调度类型。</summary>
public enum CronScheduleKind
{
    /// <summary>一次性，到点跑一次。</summary>
    Once,

    /// <summary>固定间隔。</summary>
    Interval,

    /// <summary>cron 表达式（Quartz，6/7 段；5 段会自动补秒）。</summary>
    Cron,
}

/// <summary>解析后的调度定义。</summary>
/// <param name="Kind">调度类型。</param>
/// <param name="RunAt">一次性任务的触发时间。</param>
/// <param name="Interval">固定间隔。</param>
/// <param name="Cron">规范化后的 Quartz cron 表达式。</param>
public sealed record CronScheduleSpec(
    CronScheduleKind Kind,
    DateTimeOffset? RunAt = null,
    TimeSpan? Interval = null,
    string? Cron = null);

/// <summary>
/// 调度串解析（用户和智能体都直接写这一列，所以语法要短）：
/// <list type="bullet">
/// <item><c>once:2026-10-30T09:00:00+08:00</c>（也接受 <c>2026-10-30 09:00</c>，按本地时区）——跑一次；</item>
/// <item><c>every:30m</c> / <c>every:2h</c> / <c>every:1d</c>（裸数字 = 分钟）——固定间隔；</item>
/// <item>其余当 Quartz cron 表达式：<c>0 9 * * ?</c>（5 段会自动补成 <c>0 0 9 * * ?</c>）。</item>
/// </list>
/// 纯函数，不碰调度器；时间按本地时区解释（与 Quartz 的默认时区一致）。
/// </summary>
public static class CronScheduleParser
{
    /// <summary>校验调度串；合法返回 null，非法返回可操作的说明。</summary>
    /// <param name="schedule">调度串。</param>
    public static string? Validate(string schedule)
    {
        try
        {
            Parse(schedule);
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>解析调度串；非法抛 <see cref="FormatException"/>。</summary>
    /// <param name="schedule">调度串。</param>
    public static CronScheduleSpec Parse(string schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            throw new FormatException("调度不能为空：once:<时间> / every:<间隔> / Quartz cron 表达式。");
        }

        var text = schedule.Trim();
        if (text.StartsWith("once:", StringComparison.OrdinalIgnoreCase))
        {
            var value = text["once:".Length..].Trim();
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                    out var runAt))
            {
                throw new FormatException($"一次性任务的时间无法解析：{value}（示例：once:2026-10-30T09:00:00+08:00）。");
            }

            return new CronScheduleSpec(CronScheduleKind.Once, RunAt: runAt);
        }

        if (text.StartsWith("every:", StringComparison.OrdinalIgnoreCase))
        {
            var value = text["every:".Length..].Trim();
            return new CronScheduleSpec(CronScheduleKind.Interval, Interval: ParseInterval(value));
        }

        var normalized = NormalizeCron(text);
        try
        {
            _ = new CronExpression(normalized);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new FormatException($"cron 表达式非法：{text}（{ex.Message}）。示例：0 9 * * ?。");
        }

        return new CronScheduleSpec(CronScheduleKind.Cron, Cron: normalized);
    }

    /// <summary>
    /// 严格晚于 <paramref name="after"/> 的下一次触发；一次性任务已过点返回 null，
    /// cron 五年内无匹配（如 2 月 30 日）也返回 null。
    /// </summary>
    public static DateTimeOffset? GetNext(CronScheduleSpec spec, DateTimeOffset after)
        => spec.Kind switch
        {
            CronScheduleKind.Once => spec.RunAt is { } runAt && runAt > after ? runAt : null,
            CronScheduleKind.Interval => after + (spec.Interval ?? TimeSpan.FromMinutes(1)),
            CronScheduleKind.Cron => new CronExpression(spec.Cron!).GetNextValidTimeAfter(after),
            _ => null,
        };

    /// <summary>
    /// 把 5 段 cron 补成 Quartz 的 6 段（秒补 0）；6/7 段原样返回。
    /// 用户写 <c>* * * * *</c> 时本意是「每分钟」，补秒后正好是每分钟第 0 秒。
    /// </summary>
    public static string NormalizeCron(string expression)
    {
        var fields = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length switch
        {
            5 => "0 " + string.Join(' ', fields),
            6 or 7 => string.Join(' ', fields),
            _ => throw new FormatException(
                $"cron 表达式需要 5–7 个字段（分 时 日 月 周，可选秒；Quartz 还支持年），当前 {fields.Length} 个：{expression}"),
        };
    }

    private static TimeSpan ParseInterval(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("every: 后面要写间隔：30m / 2h / 1d（裸数字 = 分钟）。");
        }

        var last = value[^1];
        if (char.IsDigit(last))
        {
            return Minutes(value);
        }

        var number = value[..^1];
        return char.ToLowerInvariant(last) switch
        {
            'm' => Minutes(number),
            'h' => Minutes(number, 60),
            'd' => Minutes(number, 1440),
            _ => throw new FormatException($"不认识的间隔单位：{value}（支持 m / h / d）。"),
        };
    }

    private static TimeSpan Minutes(string number, int factor = 1)
    {
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            throw new FormatException($"间隔必须是正整数：{number}。");
        }

        return TimeSpan.FromMinutes((long)amount * factor);
    }
}
