using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 客户端在线判定（主动消息选投递目标用）。
///
/// 只回答「现在有没有这类客户端连着」，不区分是哪一条连接：
/// 心跳只需要知道桌面在不在跑——在线就投桌面 Lane，否则退回最近的 RP IM。
/// </summary>
public interface IClientPresence
{
    /// <summary>该角色当前是否有至少一条已握手的连接。</summary>
    /// <param name="role">客户端角色。</param>
    bool IsOnline(ClientRole role);
}
