using System.Text.RegularExpressions;

namespace Shoyee.Contracts.Text;

/// <summary>
/// RP 正文的发送前清洗：模型会沿用旧习惯输出 <c>&lt;InnerVoice&gt;…&lt;/InnerVoice&gt;</c> 自检块
/// （迁移过来的历史里也有），这些内容只该进思考/日志，不该出现在 IM 或桌面上。
/// 存储仍保留原文（<c>rounds</c> 是真源），清洗只发生在出口。
/// </summary>
public static partial class MessageText
{
    /// <summary>取可发送/可展示的正文：去掉全部 InnerVoice 块、Content 包裹和残留标签。</summary>
    /// <param name="text">模型原始输出。</param>
    public static string CleanReply(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // 一次正文里可能有多个 InnerVoice 块，必须全局删，不能只删第一段
        var cleaned = InnerVoiceBlock().Replace(text, "");
        // 没闭合的（模型被截断或漏写结束标签）：从开标签起整段丢弃，避免把自检内容漏出去
        cleaned = UnclosedInnerVoice().Replace(cleaned, "");

        // 旧格式整段包在 <Content> 里时只留内层
        if (ContentBlock().Match(cleaned) is { Success: true } content)
        {
            cleaned = content.Groups[1].Value;
        }

        cleaned = StrayTag().Replace(cleaned, "");
        cleaned = BlankLines().Replace(cleaned, "\n\n");
        return cleaned.Trim();
    }

    [GeneratedRegex(@"<InnerVoice>.*?</InnerVoice>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InnerVoiceBlock();

    [GeneratedRegex(@"<InnerVoice>.*$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex UnclosedInnerVoice();

    [GeneratedRegex(@"<Content>(.*?)</Content>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ContentBlock();

    [GeneratedRegex(@"</?(?:InnerVoice|Content)>", RegexOptions.IgnoreCase)]
    private static partial Regex StrayTag();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
