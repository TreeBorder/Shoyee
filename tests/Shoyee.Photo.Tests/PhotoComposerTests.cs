using Xunit;

namespace Shoyee.Photo.Tests;

/// <summary>拼词：顺序、去重、尺寸、缺人设的提示（顺序是刻意的，见 PhotoComposer 注释）。</summary>
public sealed class PhotoComposerTests
{
    [Fact]
    public async Task Compose_JoinsInDocumentedOrderAndDeduplicates()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var composer = new PhotoComposer(PhotoTestData.Prompts(root), "xiaoyi");

            var request = await composer.ComposeAsync(
                new PhotoPlan("bedroom", "casual", "standing"),
                new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero),
                seed: 42);

            Assert.Equal(42, request.Seed);
            Assert.Equal(832, request.Width);
            Assert.Equal(1216, request.Height);
            Assert.Equal("xiaoyi", request.FileNamePrefix);
            Assert.EndsWith("quality, day-light, bedroom, angle, character, outfit, action", request.Positive);
            Assert.Contains("bad", request.Negative);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Theory]
    [InlineData("3:2", 1216, 832)]
    [InlineData("1:1", 1024, 1024)]
    [InlineData(null, 832, 1216)]
    public async Task Compose_ResolvesAspectRatio(string? ratio, int width, int height)
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var composer = new PhotoComposer(PhotoTestData.Prompts(root), "xiaoyi");

            var request = await composer.ComposeAsync(
                new PhotoPlan("bedroom", "casual", "standing", ratio),
                DateTimeOffset.UtcNow);

            Assert.Equal(width, request.Width);
            Assert.Equal(height, request.Height);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Fact]
    public async Task Compose_WithoutCharacter_ExplainsConfiguration()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var composer = new PhotoComposer(PhotoTestData.Prompts(root), null);

            var error = await Assert.ThrowsAsync<PhotoVocabularyException>(
                () => composer.ComposeAsync(
                    new PhotoPlan("bedroom", "casual", "standing"),
                    DateTimeOffset.UtcNow));

            Assert.Contains("photo.character", error.Message);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Fact]
    public async Task Compose_UnknownKey_ExplainsSupportTools()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var composer = new PhotoComposer(PhotoTestData.Prompts(root), "xiaoyi");

            var error = await Assert.ThrowsAsync<PhotoVocabularyException>(
                () => composer.ComposeAsync(
                    new PhotoPlan("bedroom", "casual", "nope"),
                    DateTimeOffset.UtcNow));

            Assert.Contains("get_support_", error.Message);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }
}
