using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Conversations;

/// <summary>按「正文 / 思考 / 工具」拆开的上下文估算。</summary>
/// <param name="Body">用户与助手正文（含系统行）。</param>
/// <param name="Reasoning">思考过程。</param>
/// <param name="Tool">工具参数与返回结果。</param>
internal readonly record struct ContextBreakdown(int Body, int Reasoning, int Tool)
{
    /// <summary>三项合计。</summary>
    public int Total => Body + Reasoning + Tool;
}

/// <summary>
/// 粗略估算上下文 token 数：中日韩字符按 1 token，其他字符按 4 字符 1 token。
///
/// 桌面端还没接 tokenizer，这个值只用来画「上下文占比」；数字后面带「≈」，
/// 真接 RPC 后改用 provider 返回的 usage 即可。
/// </summary>
internal static class TokenEstimator
{
    private const int MessageOverhead = 4;

    /// <summary>估算一整条会话已载入消息的 token 数。</summary>
    /// <param name="messages">完整消息列表（不是过滤后的可见列表）。</param>
    /// <returns>估算 token 数。</returns>
    public static int Estimate(IEnumerable<ChatMessageItem> messages) => Breakdown(messages).Total;

    /// <summary>按正文 / 思考 / 工具拆分估算，供上下文占比弹层展示。</summary>
    /// <param name="messages">完整消息列表。</param>
    /// <returns>三类 token 数。</returns>
    public static ContextBreakdown Breakdown(IEnumerable<ChatMessageItem> messages)
    {
        var body = 0;
        var reasoning = 0;
        var tool = 0;

        foreach (var message in messages)
        {
            if (message.IsReasoning)
            {
                reasoning += MessageOverhead + Estimate(message.Text);
            }
            else if (message.IsTool)
            {
                // Text 是参数摘要，已经包含在 Detail 里，不重复算。
                tool += MessageOverhead + Estimate(message.Detail) + Estimate(message.Result);
            }
            else
            {
                body += MessageOverhead + Estimate(message.Text) + message.Attachments.Count * 8;
            }
        }

        return new ContextBreakdown(body, reasoning, tool);
    }

    /// <summary>估算一段文本的 token 数。</summary>
    /// <param name="text">文本。</param>
    /// <returns>估算 token 数。</returns>
    public static int Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var wide = 0;
        var narrow = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is >= 0x2E80 and <= 0x9FFF
                or >= 0xAC00 and <= 0xD7AF
                or >= 0xF900 and <= 0xFAFF
                or >= 0xFF00 and <= 0xFFEF)
            {
                wide++;
            }
            else
            {
                narrow++;
            }
        }

        return wide + (narrow + 3) / 4;
    }

    /// <summary>把 token 数写成 1.2k / 1M 这样的短文本。</summary>
    /// <param name="tokens">token 数。</param>
    /// <returns>短文本。</returns>
    public static string Format(long tokens) => tokens switch
    {
        < 1000 => tokens.ToString(),
        < 1_000_000 => $"{tokens / 1000.0:0.#}k",
        _ => $"{tokens / 1_000_000.0:0.##}M",
    };
}
