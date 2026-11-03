namespace Shoyee.Agent.Runtime;

/// <summary>
/// 一轮的观察者：流式正文、工具过程行。Agent 不知道事件名与传输层，
/// 由 <see cref="TurnService"/> 把它翻译成 <c>turn.delta</c> / <c>turn.tool</c>。
/// </summary>
public abstract class TurnObserver
{
    /// <summary>什么都不观察。</summary>
    public static TurnObserver None { get; } = new NoneObserver();

    /// <summary>收到一段增量正文。</summary>
    /// <param name="delta">增量文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual ValueTask OnDeltaAsync(string delta, CancellationToken cancellationToken = default)
        => default;

    /// <summary>收到一段增量思考（reasoning）。</summary>
    /// <param name="delta">增量文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual ValueTask OnThinkingAsync(string delta, CancellationToken cancellationToken = default)
        => default;

    /// <summary>工具开始/结束。</summary>
    /// <param name="phase">start / end。</param>
    /// <param name="toolName">工具名。</param>
    /// <param name="summary">一句话摘要（可空）。</param>
    /// <param name="callId">模型给的调用 id。</param>
    /// <param name="arguments">参数文本（start 行）。</param>
    /// <param name="result">返回文本（end 行）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual ValueTask OnToolAsync(
        string phase,
        string toolName,
        string? summary,
        string? callId = null,
        string? arguments = null,
        string? result = null,
        CancellationToken cancellationToken = default)
        => default;

    private sealed class NoneObserver : TurnObserver
    {
    }
}
