namespace Shoyee.Web;

/// <summary>抓取结果。</summary>
/// <param name="Title">页面标题。</param>
/// <param name="Content">Markdown 正文（按参数决定是否反转义）。</param>
public sealed record WebFetchResponse(string Title, string Content);
