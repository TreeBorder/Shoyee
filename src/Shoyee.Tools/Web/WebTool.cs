using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Shoyee.Tools.Composition;
using Shoyee.Web;

namespace Shoyee.Tools.Web;

/// <summary>
/// 网页工具 <c>web_search</c> / <c>web_fetch</c>：调 DeepSeek 的 <c>/v1/tools/search</c>、<c>/v1/tools/fetch</c>，
/// 返回 JSON（搜索结果 / 页面标题 + Markdown 正文）。只读，不挂审批；两种模式都可用。
/// 不用 Harness 的 <c>HostedWebSearchTool</c>：那是模型供应方的 hosted tool，自定义 base_url 端点不一定支持。
/// </summary>
public static class WebTool
{
    /// <summary>搜索工具名。</summary>
    public const string SearchToolName = "web_search";

    /// <summary>抓取工具名。</summary>
    public const string FetchToolName = "web_fetch";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>构建两个网页工具。</summary>
    public static IReadOnlyList<ToolDescriptor> Create(IWebProvider client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var search = AIFunctionFactory.Create(
            async (
                [Description("搜索词，1-128 字符。支持 site:、-site:、\"精确短语\"、lang:、after:、before:。")] string query,
                [Description("返回条数，1-50，默认 10。")] int count = 10,
                [Description("用户原始问题（1-128 字符，可空）：帮助搜索引擎理解意图。")] string? oriQuestion = null,
                [Description("用于本地化排序的 IP 地址（可空）。")] string? localization = null,
                [Description("是否在每条结果里带整页正文（默认 false；响应会大很多）。")] bool needContent = false,
                CancellationToken cancellationToken = default) =>
            {
                var error = ValidateSearch(query, count, oriQuestion);
                if (error is not null)
                {
                    return error;
                }

                try
                {
                    var response = await client
                        .SearchAsync(query, count, oriQuestion, localization, needContent, cancellationToken)
                        .ConfigureAwait(false);
                    return JsonSerializer.Serialize(response, JsonOptions);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "网页搜索失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = SearchToolName,
                Description = "搜索网页，返回 JSON（results 数组：title/url/snippet/summary/content/siteName/date/authority/rerankScore）。需要正文时设 needContent=true。",
            });

        var fetch = AIFunctionFactory.Create(
            async (
                [Description("要抓取的 HTTP/HTTPS URL（最长 8192 字符，不能带用户名/密码）。")] string url,
                [Description("是否去掉后端 Markdown 里的反斜杠转义（默认 true；要原始文本设 false）。")] bool decodeEscapes = true,
                CancellationToken cancellationToken = default) =>
            {
                var error = ValidateUrl(url);
                if (error is not null)
                {
                    return error;
                }

                try
                {
                    var response = await client.FetchAsync(url, decodeEscapes, cancellationToken).ConfigureAwait(false);
                    return JsonSerializer.Serialize(response, JsonOptions);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "网页抓取失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = FetchToolName,
                Description = "抓取单个网页，返回 JSON（title + Markdown 正文 content）。目标站点拒绝访问或没有可提取正文时会失败。",
            });

        return
        [
            ToolDescriptor.Create(search, ToolScope.All, writes: false, requiresApproval: false),
            ToolDescriptor.Create(fetch, ToolScope.All, writes: false, requiresApproval: false),
        ];
    }

    private static string? ValidateSearch(string? query, int count, string? oriQuestion)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "web_search 需要 query。";
        }

        var length = query.EnumerateRunes().Count();
        if (length is < 1 or > 128)
        {
            return $"query 长度必须在 1-128 字符之间（当前 {length}）。";
        }

        if (count is < 1 or > 50)
        {
            return "count 必须在 1-50 之间。";
        }

        if (!string.IsNullOrWhiteSpace(oriQuestion) && oriQuestion.EnumerateRunes().Count() > 128)
        {
            return "oriQuestion 最多 128 字符。";
        }

        return null;
    }

    private static string? ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "web_fetch 需要 url。";
        }

        if (url.Length > 8192)
        {
            return "url 最多 8192 字符。";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return "url 必须是带 host 的 HTTP/HTTPS 地址。";
        }

        return string.IsNullOrEmpty(uri.UserInfo) ? null : "url 不能包含用户名/密码。";
    }
}
