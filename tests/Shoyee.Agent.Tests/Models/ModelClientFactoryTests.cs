using Microsoft.Extensions.AI;
using Shoyee.Agent.Models;
using Shoyee.Contracts.Options;
using Xunit;

namespace Shoyee.Agent.Tests.Models;

public sealed class ModelClientFactoryTests
{
    private static readonly ModelEndpoint Endpoint = new(
        "https://api.example.com/v1", "test-model", "SHOYEE_TEST_KEY");

    [Fact]
    public void Create_MissingApiKey_ThrowsWithEnvName()
    {
        var factory = new ModelClientFactory(Profiles(), _ => null);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateMain());

        Assert.Contains(Endpoint.ApiKeyEnv, error.Message, StringComparison.Ordinal);
        Assert.Contains(Endpoint.ModelId, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_InvalidBaseUrl_Throws()
    {
        var factory = new ModelClientFactory(Profiles(), _ => "key");

        Assert.Throws<ArgumentException>(() => factory.Create(Endpoint with { BaseUrl = "not-a-url" }));
    }

    [Fact]
    public void Create_WithKey_ReturnsChatClient()
    {
        var factory = new ModelClientFactory(Profiles(), _ => "key");

        using var client = factory.CreateMain();

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateVision_WithoutVisionEndpoint_ReturnsNull()
    {
        var factory = new ModelClientFactory(Profiles(), _ => "key");

        Assert.Null(factory.CreateVision());
    }

    private static ModelProfiles Profiles() => new(Endpoint, Endpoint with { ModelId = "small" });
}
