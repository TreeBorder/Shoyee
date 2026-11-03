namespace Shoyee.Contracts.Rpc;

/// <summary>JSON-RPC 错误码。与 <c>docs/IPC契约.md</c> §5 一一对应。</summary>
public static class RpcErrors
{
    /// <summary>当前连接角色不允许调用该方法。</summary>
    public const int UnauthorizedRole = -32001;

    /// <summary>参数缺失或类型错误。</summary>
    public const int InvalidParams = -32002;

    /// <summary>帧超过 8 MiB 上限。</summary>
    public const int FrameTooLarge = -32003;

    /// <summary>对象不存在。</summary>
    public const int NotFound = -32004;

    /// <summary>会话已归档。</summary>
    public const int SessionArchived = -32005;

    /// <summary>需要单飞的写操作正在执行。</summary>
    public const int Busy = -32006;

    /// <summary>模型端点不可用（可重试）。</summary>
    public const int ModelUnavailable = -32007;

    /// <summary>同一会话已有待审批请求。</summary>
    public const int ApprovalPending = -32008;

    /// <summary>主版本不兼容。</summary>
    public const int IncompatibleVersion = -32010;

    /// <summary>方法未注册 / 本版本未实现（JSON-RPC 2.0 标准码）。</summary>
    public const int MethodNotFound = -32601;

    /// <summary>其他内部错误。</summary>
    public const int Internal = -32011;

    /// <summary>按错误码返回稳定的名称。</summary>
    public static string NameOf(int code) => code switch
    {
        UnauthorizedRole => "unauthorized_role",
        InvalidParams => "invalid_params",
        FrameTooLarge => "frame_too_large",
        NotFound => "not_found",
        SessionArchived => "session_archived",
        Busy => "busy",
        ModelUnavailable => "model_unavailable",
        ApprovalPending => "approval_pending",
        IncompatibleVersion => "incompatible_version",
        MethodNotFound => "method_not_found",
        Internal => "internal",
        _ => "unknown",
    };
}
