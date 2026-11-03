using Shoyee.Contracts.Text;

namespace Shoyee.Sessions.Lifecycle;

/// <summary>
/// 消息展示清洗：给桌面/客户端看的只有正文，注入给模型的标签不出现在界面上。
///
/// 存储里保留原文（<c>rounds</c> 是 RP 的真源，检索/上下文都用原文），清洗只发生在
/// <c>MessageView.Text</c> 出口。只处理**开头**的注入段，避免误伤用户正文里恰好出现的同名标签。
///
/// 已知注入段（见 <c>RoleplayContextBuilder.BuildInputMessage</c>）：
/// <c>&lt;Time&gt;[消息时间: …]&lt;/Time&gt;</c>、<c>&lt;Channel&gt;[渠道: …]&lt;/Channel&gt;</c>、
/// 整段包裹的 <c>&lt;Summary&gt;…&lt;/Summary&gt;</c>。
/// </summary>
public static class MessageDisplay
{
    private const string TimeOpen = "<Time>";
    private const string TimeClose = "</Time>";
    private const string ChannelOpen = "<Channel>";
    private const string ChannelClose = "</Channel>";
    private const string SummaryOpen = "<Summary>";
    private const string SummaryClose = "</Summary>";

    /// <summary>取可显示的正文。</summary>
    /// <param name="content">存储里的原始正文。</param>
    public static string Clean(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var text = content;
        while (true)
        {
            if (text.StartsWith(TimeOpen, StringComparison.Ordinal))
            {
                var end = text.IndexOf(TimeClose, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                text = text[(end + TimeClose.Length)..];
                continue;
            }

            if (text.StartsWith(ChannelOpen, StringComparison.Ordinal))
            {
                var end = text.IndexOf(ChannelClose, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                text = text[(end + ChannelClose.Length)..];
                continue;
            }

            break;
        }

        if (text.StartsWith(SummaryOpen, StringComparison.Ordinal)
            && text.EndsWith(SummaryClose, StringComparison.Ordinal)
            && text.Length >= SummaryOpen.Length + SummaryClose.Length)
        {
            text = text[SummaryOpen.Length..^SummaryClose.Length];
        }

        return MessageText.CleanReply(text);
    }
}
