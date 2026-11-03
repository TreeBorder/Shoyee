using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>消息种类；只有 Text / Spoken / ToolCall 的正文参与检索。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<MessageKind>))]
public enum MessageKind
{
    /// <summary>无法归类（不检索，按过程行显示）。</summary>
    Unknown,

    /// <summary>普通可见文本。</summary>
    Text,

    /// <summary>工具调用（过程行）；调用前的正文仍可检索。</summary>
    ToolCall,

    /// <summary>工具结果（过程行，不写正文）。</summary>
    ToolResult,

    /// <summary>压缩/摘要标记消息。</summary>
    Summary,

    /// <summary>她主动发出的可见消息（心跳、新话题），与普通文本同权。</summary>
    Spoken,

    /// <summary>
    /// 系统注入（心跳、定时任务、后台进程回执）：以用户身份进模型上下文，
    /// 投影成独立种类——桌面默认隐藏，可在「系统消息」开关里当用户消息查看；不检索。
    /// </summary>
    SystemInput,
}
