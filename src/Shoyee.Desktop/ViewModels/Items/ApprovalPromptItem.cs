namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>
/// 桌面收到的待审批请求（<c>approval.request</c>）。
///
/// 审批在服务端是「回合内挂起」：不答复这一轮不会结束，超时按拒绝收束。
/// 桌面只负责显示工具名与命令原文，并把结论发回。
/// </summary>
public sealed class ApprovalPromptItem
{
    /// <summary>审批 id。</summary>
    public required string ApprovalId { get; init; }

    /// <summary>所属轮次。</summary>
    public required string TurnId { get; init; }

    /// <summary>工具名。</summary>
    public required string ToolName { get; init; }

    /// <summary>命令裸原文（非命令类工具为 null）。</summary>
    public string? Command { get; init; }

    /// <summary>完整参数（JSON 文本；命令类工具里 command 仍是原文）。</summary>
    public string? Arguments { get; init; }

    /// <summary>过期时间（本地时分）。</summary>
    public string ExpiresText { get; init; } = "";

    /// <summary>是否展示命令原文。</summary>
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);

    /// <summary>是否展示完整参数。</summary>
    public bool HasArguments => !string.IsNullOrWhiteSpace(Arguments);

    /// <summary>标题。</summary>
    public string Title => $"需要确认：{ToolName}";

    /// <summary>过期提示。</summary>
    public string ExpiryHint => string.IsNullOrWhiteSpace(ExpiresText)
        ? "不答复会在超时后按拒绝处理"
        : $"{ExpiresText} 前不答复按拒绝处理";
}
