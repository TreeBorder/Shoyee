using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 渠道发送失败：适配器把「平台语义」翻译成 <see cref="ChannelSendFailure"/>，投递台账据此决定重试策略。
///
/// 刻意不做成供应商错误码：微信的 <c>ret:-2 prepare failed</c>、Mattermost 的 404 都应该收敛到这几类，
/// 否则通用层迟早要认识每个平台。
/// </summary>
public sealed class ChannelSendException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="failure">失败类别。</param>
    /// <param name="message">给人看的说明。</param>
    /// <param name="innerException">内层异常（可空）。</param>
    public ChannelSendException(ChannelSendFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
        => Failure = failure;

    /// <summary>失败类别。</summary>
    public ChannelSendFailure Failure { get; }
}
