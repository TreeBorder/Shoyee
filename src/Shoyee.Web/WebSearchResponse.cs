namespace Shoyee.Web;

/// <summary>网页搜索结果集。</summary>
/// <param name="Results">按相关度排序的命中。</param>
public sealed record WebSearchResponse(IReadOnlyList<WebSearchHit> Results);
