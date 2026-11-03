using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>消息角色。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<MessageRole>))]
public enum MessageRole
{
    /// <summary>无法识别的角色（线上保留原值，DTO 层退回这里）。</summary>
    Unknown,

    /// <summary>用户。</summary>
    User,

    /// <summary>她。</summary>
    Assistant,

    /// <summary>系统注入（摘要标记等）。</summary>
    System,

    /// <summary>工具结果。</summary>
    Tool,
}
