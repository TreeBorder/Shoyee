using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>IPC 连接角色，决定服务端强制的事件可见性与可调方法（不是 UI 自觉）。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ClientRole>))]
public enum ClientRole
{
    /// <summary>桌面 RP 客户端：只看自己的 Lane。</summary>
    DesktopRoleplay,

    /// <summary>桌面工作客户端：可看全部工作会话。</summary>
    DesktopWork,

    /// <summary>Swift 菜单栏：只看状态。</summary>
    Menu,

    /// <summary>shoyee-ctl：状态与运维方法。</summary>
    Ctl,

    /// <summary>测试连接（仅测试环境注册）。</summary>
    Test,
}
