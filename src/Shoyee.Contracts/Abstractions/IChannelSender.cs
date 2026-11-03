namespace Shoyee.Contracts.Abstractions;

/// <summary>出站发送面：渠道适配器实现，投递装饰层调用。</summary>
public interface IChannelSender
{
    /// <summary>该发送器负责的 Lane id。</summary>
    string LaneId { get; }

    /// <summary>
    /// 发送结果是否可信（默认 true）。
    ///
    /// false = 「不可信渠道」：超时/平台会自己重发/回执含糊，**重发有重复投递的风险**。
    /// 台账对它只尝试一次，成功失败都按已送达销账，绝不再投；是否补发由适配器自己决定。
    /// </summary>
    bool OutcomeTrusted => true;

    /// <summary>发送一条文本（渠道自行分段/限长）。</summary>
    Task SendTextAsync(string target, string text, CancellationToken cancellationToken = default);

    /// <summary>发送一个本地文件。</summary>
    Task SendFileAsync(string target, string filePath, string? caption, CancellationToken cancellationToken = default);
}
