using System.Text;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// 指令回复的表格排版：统一转义单元格、统一时间格式。
///
/// 渠道渲染 Markdown（Mattermost / 微信 / 桌面），所以指令回复一律用表格表达二维内容，
/// 换行由表格承担；表格之外的「详情：」只放 id、完整调度串、错误原因这类长值。
/// </summary>
internal static class TableText
{
    /// <summary>单元格：竖线和换行会把列拆坏，换成全角竖线/空格；空值统一写 <c>—</c>。</summary>
    /// <param name="text">原始文本。</param>
    public static string Cell(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? "—"
            : text.Replace('|', '｜').ReplaceLineEndings(" ").Trim();

    /// <summary>本地时间（<c>MM-dd HH:mm</c>）；null 写 <c>—</c>。</summary>
    /// <param name="at">UTC 时间。</param>
    public static string Time(DateTimeOffset? at)
        => at is { } value ? value.ToLocalTime().ToString("MM-dd HH:mm") : "—";

    /// <summary>渲染 Markdown 表格（第一行为表头）。</summary>
    /// <param name="headers">表头。</param>
    /// <param name="rows">数据行；列数应与表头一致。</param>
    public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRow(builder, headers);
        AppendRow(builder, headers.Select(static _ => "---").ToList());
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> cells)
        => builder
            .Append('|')
            .Append(string.Join('|', cells.Select(static cell => " " + cell + " ")))
            .AppendLine("|");
}
