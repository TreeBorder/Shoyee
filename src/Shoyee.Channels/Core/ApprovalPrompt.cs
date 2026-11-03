using System.Text;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Channels.Core;

/// <summary>
/// 审批提示文本（IM 侧）。只列工具与命令原文；参数 JSON 太长，IM 里没有阅读价值。
/// 文本与桌面事件内容一致，不另造一套说法。
/// </summary>
public static class ApprovalPrompt
{
    private const int MaxCommandLength = 300;

    /// <summary>把一批待审批调用渲染成一条 IM 提示。</summary>
    /// <param name="requests">待审批请求（同一批）。</param>
    public static string Format(IReadOnlyList<ApprovalRequestDto> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var builder = new StringBuilder();
        if (requests.Count == 1)
        {
            builder.Append("⚠️ 有个操作等你批准：").AppendLine(Describe(requests[0]));
        }
        else
        {
            builder.Append("⚠️ 有 ").Append(requests.Count).AppendLine(" 个操作等你批准：");
            for (var index = 0; index < requests.Count; index++)
            {
                builder.Append(index + 1).Append(". ").AppendLine(Describe(requests[index]));
            }
        }

        builder.Append(requests.Count == 1
            ? "回复 /approve 批准，/approve always 本会话内总是允许，/reject 拒绝。"
            : "回复 /approve 全部批准，/approve always 本会话内总是允许，/reject 全部拒绝。");
        return builder.ToString();
    }

    private static string Describe(ApprovalRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return request.ToolName;
        }

        var command = request.Command.Trim();
        if (command.Length > MaxCommandLength)
        {
            command = command[..MaxCommandLength] + "…";
        }

        return request.ToolName + "：" + command;
    }
}
