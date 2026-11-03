using Microsoft.Extensions.AI;

namespace Shoyee.Sessions.Turns;

/// <summary>
/// 悬空工具调用修补：给「有调用、无结果」的 <see cref="FunctionCallContent"/> 补一条合成结果。
///
/// 两种模式都要用——
/// - RP：中断/崩溃后，本轮响应里可能只留下调用；不补的话下一轮发给 OpenAI 兼容端点直接 400。
/// - 工作：中断时框架 session 的历史里同样是「assistant 带调用、没有 tool 结果」，
///   在保存/恢复 session 前也得补，否则 resume 后第一轮就失败。
///
/// 纯函数，不碰存储，也不假设消息来自哪种模式。
/// </summary>
public static class ToolCallRepair
{
    /// <summary>合成结果的默认正文。</summary>
    public const string InterruptedResultText = "（工具调用未完成：本轮已中断）";

    /// <summary>为缺失结果的工具调用补 <see cref="FunctionResultContent"/>；没有缺失时原样返回。</summary>
    /// <param name="messages">本轮累积的响应消息（RP）或 session 历史（工作）。</param>
    /// <param name="resultText">合成结果正文。</param>
    public static IReadOnlyList<ChatMessage> RepairDanglingToolCalls(
        IReadOnlyList<ChatMessage> messages,
        string resultText = InterruptedResultText)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var answered = new HashSet<string>(StringComparer.Ordinal);
        var callIds = new List<string>();
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionResultContent result when !string.IsNullOrEmpty(result.CallId):
                        answered.Add(result.CallId);
                        break;
                    case FunctionCallContent call when !string.IsNullOrEmpty(call.CallId):
                        callIds.Add(call.CallId);
                        break;
                }
            }
        }

        var missing = callIds
            .Where(id => !answered.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0)
        {
            return messages;
        }

        var repaired = new List<ChatMessage>(messages.Count + missing.Count);
        repaired.AddRange(messages);
        foreach (var callId in missing)
        {
            repaired.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, resultText)]));
        }

        return repaired;
    }
}
