namespace Shoyee.Tools.Photo;

/// <summary>
/// 当前 RP 轮次所在的 Lane（由回合运行时在进入 agent 前注入），供异步出图完成后把图推回原 Lane。
/// 与 <see cref="Process.ProcessScope"/> 同一思路：工具是单例，只有回合执行流里才知道「现在是谁」。
/// </summary>
public static class PhotoScope
{
    private static readonly AsyncLocal<string?> Key = new();

    /// <summary>当前 Lane id；不在 RP 轮次里时为 null。</summary>
    public static string? LaneId
    {
        get => Key.Value;
        set => Key.Value = value;
    }
}
