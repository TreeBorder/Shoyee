using Microsoft.Extensions.AI;
using Shoyee.Tools.Web;
using Shoyee.Web;
using Xunit;

namespace Shoyee.Tools.Tests.Web;

public sealed class WebToolTests
{
    [Fact]
    public async Task WebFetch_RejectsNonHttpUrl()
    {
        var tool = (AIFunction)WebTool.Create(new NeverCalledProvider())
            .Single(t => t.Metadata.Name == WebTool.FetchToolName).Tool;

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["url"] = "ftp://example.com" });

        Assert.Contains("HTTP/HTTPS", result?.ToString());
    }

    private sealed class NeverCalledProvider : IWebProvider
    {
        public Task<WebSearchResponse> SearchAsync(
            string query,
            int count = 10,
            string? oriQuestion = null,
            string? localization = null,
            bool needContent = false,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("不应调用");

        public Task<WebFetchResponse> FetchAsync(
            string url,
            bool decodeEscapes = true,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("不应调用");
    }
}
