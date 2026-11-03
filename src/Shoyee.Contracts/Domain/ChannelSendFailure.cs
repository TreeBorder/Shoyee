using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>
/// 发送失败的类别：决定投递台账怎么处理，而不是让通用层去猜供应商的错误码。
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ChannelSendFailure>))]
public enum ChannelSendFailure
{
    /// <summary>暂时性失败（网络/服务端 5xx）：按指数退避重试。</summary>
    Transient,

    /// <summary>渠道的发送窗口关闭（微信「用户来消息」窗口）：不按秒重试，等该 Lane 再次入站时补发。</summary>
    WindowClosed,

    /// <summary>会话/令牌过期：同样等下次入站刷新后再补，不在后台空转。</summary>
    SessionExpired,

    /// <summary>确定性失败（参数错、目标不存在、结果不确定但重发有副作用）：直接放弃。</summary>
    Permanent,
}
