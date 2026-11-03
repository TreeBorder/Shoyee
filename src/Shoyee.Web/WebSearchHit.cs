namespace Shoyee.Web;

/// <summary>一条网页搜索结果（字段与 DeepSeek <c>/v1/tools/search</c> 对齐）。</summary>
/// <param name="Title">标题。</param>
/// <param name="Url">链接。</param>
/// <param name="Snippet">摘要片段。</param>
/// <param name="Summary">较长摘要。</param>
/// <param name="Content">正文（仅 <c>needContent=true</c> 时非空）。</param>
/// <param name="SiteName">站点名。</param>
/// <param name="Date">发布时间（原样字符串）。</param>
/// <param name="RerankScore">重排分数。</param>
/// <param name="Authority">权威度。</param>
public sealed record WebSearchHit(
    string Title,
    string Url,
    string? Snippet,
    string? Summary,
    string Content,
    string? SiteName,
    string? Date,
    double RerankScore,
    int Authority);
