using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>用户可见模式。模式是会话属性，不是进程属性。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<Mode>))]
public enum Mode
{
    /// <summary>角色扮演。</summary>
    Roleplay,

    /// <summary>工作。</summary>
    Work,
}
