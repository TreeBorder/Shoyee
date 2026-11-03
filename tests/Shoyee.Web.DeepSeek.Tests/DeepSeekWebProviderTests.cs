using System.Net;
using System.Text;
using Shoyee.Contracts.Options;
using Shoyee.Web.DeepSeek;
using Xunit;

namespace Shoyee.Web.DeepSeek.Tests;

public sealed class DeepSeekWebProviderTests
{
    private const string KeyEnv = "SHOYEE_TEST_WEB_KEY";

    [Fact]
    public async Task Search_SendsAuthAndParsesSnakeCaseFields()
    {
        Environment.SetEnvironmentVariable(KeyEnv, "sk-test");
        try
        {
            string? path = null;
            string? auth = null;
            var handler = new StubHandler(request =>
            {
                path = request.RequestUri?.AbsolutePath;
                auth = request.Headers.Authorization?.ToString();
                return Json(
                    """{"results":[{"title":"标题","url":"https://example.com","snippet":"片段","summary":null,"content":"","site_name":"站点","date":"2026-01-01","rerank_score":0.9,"authority":7}]}""");
            });
            var provider = Provider(handler);

            var response = await provider.SearchAsync("测试", count: 3, needContent: false);

            Assert.Equal("/v1/tools/search", path);
            Assert.Equal("Bearer sk-test", auth);
            var hit = Assert.Single(response.Results);
            Assert.Equal("标题", hit.Title);
            Assert.Equal("站点", hit.SiteName);
            Assert.Equal(7, hit.Authority);
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyEnv, null);
        }
    }

    [Fact]
    public async Task Fetch_UnescapesMarkdownByDefault()
    {
        Environment.SetEnvironmentVariable(KeyEnv, "sk-test");
        try
        {
            var provider = Provider(new StubHandler(_ => Json(
                """{"title":"页","content":"a\\.b\\-c\\/d"}""")));

            var response = await provider.FetchAsync("https://example.com/a");

            Assert.Equal("页", response.Title);
            Assert.Equal("a.b-c/d", response.Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyEnv, null);
        }
    }

    [Fact]
    public async Task MissingKey_ReturnsActionableError()
    {
        Environment.SetEnvironmentVariable(KeyEnv, null);
        var provider = Provider(new StubHandler(_ => Json("""{"results":[]}""")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SearchAsync("测试"));

        Assert.Contains("API key", error.Message);
    }

    private static DeepSeekWebProvider Provider(HttpMessageHandler handler)
        => new(
            new DeepSeekWebOptions("https://api.deepseek.com", KeyEnv, 5),
            new ModelProfiles(
                new ModelEndpoint("https://api.deepseek.com", "deepseek-flash", KeyEnv),
                new ModelEndpoint("https://api.deepseek.com", "deepseek-flash", KeyEnv)),
            new HttpClient(handler));

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
