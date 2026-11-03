namespace Shoyee.Web;

/// <summary>网页搜索/抓取能力（供应商无关；具体实现在 <c>Shoyee.Web.*</c>）。</summary>
public interface IWebProvider
{
    /// <summary>网页搜索。</summary>
    Task<WebSearchResponse> SearchAsync(
        string query,
        int count = 10,
        string? oriQuestion = null,
        string? localization = null,
        bool needContent = false,
        CancellationToken cancellationToken = default);

    /// <summary>抓取单页，返回 Markdown 正文（是否反转义由实现按参数处理）。</summary>
    Task<WebFetchResponse> FetchAsync(
        string url,
        bool decodeEscapes = true,
        CancellationToken cancellationToken = default);
}
