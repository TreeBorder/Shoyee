using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>投递台账状态。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<DeliveryState>))]
public enum DeliveryState
{
    /// <summary>待发/待补发。</summary>
    Pending,

    /// <summary>已送达。</summary>
    Sent,

    /// <summary>失败可重试。</summary>
    Failed,

    /// <summary>放弃（不可重试或超出重试上限）。</summary>
    Abandoned,
}
