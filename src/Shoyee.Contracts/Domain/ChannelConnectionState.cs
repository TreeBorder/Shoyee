using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>渠道连接状态（<c>status.get</c> 的 <see cref="Dtos.LaneStatus.Connected"/> 取这里）。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ChannelConnectionState>))]
public enum ChannelConnectionState
{
    /// <summary>未启动或已停止。</summary>
    Disconnected,

    /// <summary>正在连接/重连。</summary>
    Connecting,

    /// <summary>已连接，可以收消息。</summary>
    Connected,

    /// <summary>配置无效或不可恢复的失败（需要人处理）。</summary>
    Failed,
}
