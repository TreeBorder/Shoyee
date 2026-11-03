namespace Shoyee.Tools.Process;

/// <summary>
/// 当前工具调用的会话键（由回合运行时在进入 agent 前注入），供后台进程把回执送回原对话。
/// </summary>
public static class ProcessScope
{
    private static readonly AsyncLocal<string?> Key = new();

    /// <summary>当前会话键；不在会话流里时为 null。</summary>
    public static string? SessionKey
    {
        get => Key.Value;
        set => Key.Value = value;
    }
}
