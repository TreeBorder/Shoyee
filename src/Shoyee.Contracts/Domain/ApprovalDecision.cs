using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>审批结论。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ApprovalDecision>))]
public enum ApprovalDecision
{
    /// <summary>本次允许。</summary>
    Approved,

    /// <summary>本次拒绝。</summary>
    Denied,

    /// <summary>本会话内总是允许。</summary>
    Always,

    /// <summary>超时未答复，按拒绝处理。</summary>
    Expired,
}
