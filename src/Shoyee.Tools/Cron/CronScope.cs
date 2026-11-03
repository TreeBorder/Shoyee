namespace Shoyee.Tools.Cron;

/// <summary>
/// 当前 RP 轮次的 Lane（<c>cron_add</c> 在 RP 模式下来源 Lane 的默认目标）。
/// 由 <c>RoleplayTurnRunner</c> 在每轮开始时设置，和 <c>PhotoScope</c> 同一手法。
/// </summary>
public static class CronScope
{
    private static readonly AsyncLocal<string?> Key = new();

    /// <summary>当前轮次的 Lane id（工作模式/无轮次时为 null）。</summary>
    public static string? LaneId
    {
        get => Key.Value;
        set => Key.Value = value;
    }
}
