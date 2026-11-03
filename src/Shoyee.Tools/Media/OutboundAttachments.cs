using Shoyee.Contracts.Dtos;

namespace Shoyee.Tools.Media;

/// <summary>
/// 本轮工具产出的出站附件（语音、图片、显式发送的文件）。
///
/// 工具在调用时往里放，轮次结束由运行时取走并挂到**最后一条可见助手消息**上：
/// 附件属于「她这一轮给了你什么」，不是工具调用的返回值，也不该另造一条空消息。
/// </summary>
public sealed class OutboundAttachments
{
    private readonly List<MessageAttachment> _items = [];

    /// <summary>已收集的附件（按产出顺序）。</summary>
    public IReadOnlyList<MessageAttachment> Items => _items;

    /// <summary>记录一个附件；同一路径重复记录只留第一次。</summary>
    /// <param name="attachment">附件。</param>
    public void Add(MessageAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (_items.Exists(item => string.Equals(item.Path, attachment.Path, StringComparison.Ordinal)))
        {
            return;
        }

        _items.Add(attachment);
    }
}

/// <summary>
/// 当前轮次的出站附件收集器（<see cref="AsyncLocal{T}"/>）。
///
/// 与 <see cref="Photo.PhotoScope"/> 同一手法：工具是单例，轮次上下文只能用环境变量传。
/// 没设收集器（不在轮次里调用工具）时工具照常返回路径，只是不挂到消息上。
/// </summary>
public static class OutboundScope
{
    private static readonly AsyncLocal<OutboundAttachments?> Holder = new();

    /// <summary>当前轮次的收集器；null = 不在轮次内。</summary>
    public static OutboundAttachments? Current
    {
        get => Holder.Value;
        set => Holder.Value = value;
    }
}
