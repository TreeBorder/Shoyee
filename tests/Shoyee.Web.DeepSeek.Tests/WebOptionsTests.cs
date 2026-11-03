using System.Text.Json;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Xunit;

namespace Shoyee.Web.DeepSeek.Tests;

public sealed class WebOptionsTests
{
    [Fact]
    public void ProviderSection_IsReadByProviderKey()
    {
        var web = JsonSerializer.Deserialize<WebOptions>(
            """
            {
              "provider": "deepseek",
              "providers": {
                "deepseek": { "baseUrl": "https://example.test", "apiKeyEnv": "MY_KEY", "timeoutSeconds": 9 }
              }
            }
            """,
            JsonWire.Options)!;

        var options = web.GetProviderOptions<DeepSeekWebOptions>("deepseek")!;

        Assert.Equal("https://example.test", options.BaseUrl);
        Assert.Equal("MY_KEY", options.ApiKeyEnv);
        Assert.Equal(9, options.TimeoutSeconds);
    }

    [Fact]
    public void MissingSection_FallsBackToDefaults()
    {
        var web = new WebOptions();

        Assert.Null(web.GetProviderOptions<DeepSeekWebOptions>("deepseek"));
    }
}
