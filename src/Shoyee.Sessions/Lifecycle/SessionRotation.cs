namespace Shoyee.Sessions.Lifecycle;

/// <summary>
/// 工作会话的跨天轮转判定（沿用 Secretary 规则）：
/// 同一本地日不换；跨本地日且空闲 ≥ <see cref="MinIdle"/> 才换。
///
/// 「每天换新」不是「每 24 小时换新」——23:50 聊到 00:30 仍算同一场对话。
/// 桌面操作不触发轮转，只有入站消息到达时才判定（调用方负责）。
/// </summary>
public static class SessionRotation
{
    /// <summary>跨本地日后仍需空闲这么久才轮转（防跨零点抖动）。</summary>
    public static readonly TimeSpan MinIdle = TimeSpan.FromHours(2);

    /// <summary>该会话在 <paramref name="now"/> 是否该轮转。</summary>
    /// <param name="lastActiveAt">最后活跃时间。</param>
    /// <param name="now">判定时刻。</param>
    public static bool IsStale(DateTimeOffset lastActiveAt, DateTimeOffset now)
    {
        var lastLocal = TimeZoneInfo.ConvertTime(lastActiveAt, TimeZoneInfo.Local);
        var nowLocal = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        if (lastLocal.Date == nowLocal.Date)
        {
            return false;
        }

        // 时间倒流（测试传过去时刻/系统时钟回拨）不误切
        return now - lastActiveAt >= MinIdle;
    }
}
