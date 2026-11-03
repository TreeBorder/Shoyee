using System.Text;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Ctl.Cli;

/// <summary>把 <see cref="DaemonStatusDto"/> 排成终端可读的一段话（菜单栏走 JSON，不用这里）。</summary>
public static class StatusReport
{
    /// <summary>渲染状态。</summary>
    /// <param name="status">daemon 状态。</param>
    public static string Format(DaemonStatusDto status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var text = new StringBuilder();
        text.Append("筱 运行中 · v").Append(status.Version)
            .Append(" · 已运行 ").Append(FormatDuration(TimeSpan.FromSeconds(status.UptimeSeconds)))
            .AppendLine();
        text.Append("socket  ").AppendLine(status.SocketPath);
        text.AppendLine();
        foreach (var lane in status.Lanes)
        {
            text.Append(lane.Connected ? "● " : "○ ")
                .Append(DisplayName(lane))
                .Append(" (").Append(ModeName(lane.Mode)).Append(")  ")
                .Append(lane.Id);
            if (!string.IsNullOrWhiteSpace(lane.ActiveSessionId))
            {
                text.Append("  · 会话 ").Append(lane.ActiveSessionId);
            }

            if (!string.IsNullOrWhiteSpace(lane.LastError))
            {
                text.Append("  — ").Append(lane.LastError);
            }

            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>人类可读的时长（天/小时/分，最少给秒）。</summary>
    /// <param name="duration">时长。</param>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays} 天 {duration.Hours} 小时";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分";
        }

        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes} 分 {duration.Seconds} 秒"
            : $"{(int)duration.TotalSeconds} 秒";
    }

    private static string DisplayName(LaneStatus lane)
        => string.IsNullOrWhiteSpace(lane.DisplayName) ? ChannelName(lane.Channel) : lane.DisplayName;

    private static string ChannelName(LaneChannel channel) => channel switch
    {
        LaneChannel.Desktop => "桌面",
        LaneChannel.Mattermost => "Mattermost",
        LaneChannel.WeChat => "微信",
        _ => channel.ToString(),
    };

    private static string ModeName(Mode mode) => mode == Mode.Roleplay ? "RP" : "工作";
}
