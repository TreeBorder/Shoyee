namespace Shoyee.Contracts.Rpc;

/// <summary><c>turn.failed</c> 的 <c>code</c> 取值（与 JSON-RPC 错误码是两套：这里描述轮次结局）。</summary>
public static class TurnFailureCodes
{
    /// <summary>被 <c>turn.cancel</c> 打断（停就停，已落盘内容不回滚）。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>模型端点不可用（可重试）。</summary>
    public const string ModelUnavailable = "model_unavailable";

    /// <summary>其他内部错误。</summary>
    public const string Internal = "internal";
}
