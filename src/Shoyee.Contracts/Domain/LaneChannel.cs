using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>渠道类型；桌面本身也是一条 Lane。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<LaneChannel>))]
public enum LaneChannel
{
    /// <summary>桌面。</summary>
    Desktop,

    /// <summary>Mattermost（双 token：RP / 工作各一条 Lane）。</summary>
    Mattermost,

    /// <summary>微信（只做工作模式）。</summary>
    WeChat,
}
