using System.Globalization;

namespace Shoyee.Desktop.ViewModels.Cron;

/// <summary>定时任务页共用的展示文案。</summary>
internal static class CronText
{
    /// <summary>把 <c>once:</c> / <c>every:</c> / Quartz cron 翻成人话。</summary>
    public static string Schedule(string schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            return "—";
        }

        var trimmed = schedule.Trim();
        if (trimmed.StartsWith("once:", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.TryParse(
                trimmed[5..],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var once)
                ? $"一次性 · {once.ToLocalTime():MM-dd HH:mm}"
                : $"一次性 · {trimmed[5..]}";
        }

        if (trimmed.StartsWith("every:", StringComparison.OrdinalIgnoreCase))
        {
            return "每 " + Interval(trimmed[6..]);
        }

        return $"cron · {trimmed}";
    }

    /// <summary>执行方式。</summary>
    public static string Kind(string kind) => kind switch
    {
        "prompt" => "跑模型",
        "script" => "跑命令",
        "message" => "固定消息",
        _ => kind,
    };

    /// <summary>附加投递。</summary>
    public static string Deliver(string deliver)
    {
        var parts = (deliver ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part switch
            {
                "push" => "推送通知",
                "local" => "只记台账",
                _ => part,
            });
        return string.Join(" · ", parts);
    }

    /// <summary>上次运行状态。</summary>
    public static string Status(string? status) => status switch
    {
        "succeeded" => "成功",
        "failed" => "失败",
        "running" => "进行中",
        null or "" => "未运行",
        _ => status,
    };

    /// <summary>执行记录里的状态。</summary>
    public static string State(string? state) => Status(state);

    /// <summary>下次触发：本地时间 + 相对描述。</summary>
    public static string NextRun(DateTimeOffset? next)
    {
        if (next is not { } value)
        {
            return "—";
        }

        var local = value.ToLocalTime();
        var delta = local - DateTimeOffset.Now;
        if (delta < TimeSpan.Zero)
        {
            return $"{local:MM-dd HH:mm} · 已过期";
        }

        return $"{local:MM-dd HH:mm} · {Relative(delta)}后";
    }

    /// <summary>本地时间（没有值时给破折号）。</summary>
    public static string Time(DateTimeOffset? value)
        => value is { } time ? time.ToLocalTime().ToString("MM-dd HH:mm") : "—";

    /// <summary>执行耗时。</summary>
    public static string Duration(DateTimeOffset? started, DateTimeOffset? finished)
    {
        if (started is not { } start)
        {
            return "—";
        }

        if (finished is not { } end)
        {
            return "进行中";
        }

        var delta = end - start;
        if (delta < TimeSpan.FromSeconds(1))
        {
            return "<1 秒";
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return $"{delta.TotalSeconds:0} 秒";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{delta.TotalMinutes:0.#} 分钟";
        }

        return $"{delta.TotalHours:0.#} 小时";
    }

    private static string Interval(string value)
    {
        if (value.Length > 1 && int.TryParse(value[..^1], out var number))
        {
            var unit = value[^1] switch
            {
                's' => "秒",
                'm' => "分钟",
                'h' => "小时",
                'd' => "天",
                _ => null,
            };
            if (unit is not null)
            {
                return $"{number} {unit}";
            }
        }

        return value;
    }

    private static string Relative(TimeSpan delta)
    {
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "不到 1 分钟";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{delta.TotalMinutes:0} 分钟";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{delta.TotalHours:0.#} 小时";
        }

        return $"{delta.TotalDays:0.#} 天";
    }
}
