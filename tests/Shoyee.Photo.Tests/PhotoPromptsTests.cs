using Xunit;

namespace Shoyee.Photo.Tests;

/// <summary>词表读取：条目、清单、环境按时段回落、缺文件的可操作提示。</summary>
public sealed class PhotoPromptsTests
{
    [Fact]
    public async Task GetAndList_ReadEditableVocabulary()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var prompts = PhotoTestData.Prompts(root);

            var block = await prompts.GetAsync(PhotoPrompts.ActionKind, "standing");
            Assert.NotNull(block);
            Assert.Equal("站立", block.DisplayName);
            Assert.Equal(["action"], block.PositivePrompts);

            var entries = await prompts.ListAsync(PhotoPrompts.SceneKind);
            Assert.Contains(entries, e => e.Key == "bedroom" && e.Name == "卧室");
            Assert.True(await prompts.HasAsync(PhotoPrompts.SceneKind, "studio"));
            Assert.False(await prompts.HasAsync(PhotoPrompts.SceneKind, "nope"));
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Theory]
    [InlineData(8, "indoor_morning")]
    [InlineData(13, "indoor_day")]
    [InlineData(22, "indoor")]
    public async Task ResolveEnvironment_UsesCategoryAndTime(int hour, string expected)
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var prompts = PhotoTestData.Prompts(root);

            var key = await prompts.ResolveEnvironmentKeyAsync(
                "bedroom",
                new DateTimeOffset(2026, 10, 28, hour, 0, 0, TimeSpan.Zero));

            Assert.Equal(expected, key);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Fact]
    public async Task ResolveEnvironment_MorningFallsBackToDay()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root, withMorning: false);
            var key = await PhotoTestData.Prompts(root).ResolveEnvironmentKeyAsync(
                "bedroom",
                new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.Zero));

            Assert.Equal("indoor_day", key);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Fact]
    public async Task ResolveEnvironment_StudioAndUnknownCategory()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            PhotoTestData.WriteVocabulary(root);
            var prompts = PhotoTestData.Prompts(root);
            var now = new DateTimeOffset(2026, 10, 28, 13, 0, 0, TimeSpan.Zero);

            Assert.Equal("studio", await prompts.ResolveEnvironmentKeyAsync("studio", now));
            Assert.Equal("default", await prompts.ResolveEnvironmentKeyAsync("missing-scene", now));
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }

    [Fact]
    public async Task MissingFile_ExplainsWhereTemplatesComeFrom()
    {
        var root = PhotoTestData.NewRoot();
        try
        {
            var error = await Assert.ThrowsAsync<PhotoVocabularyException>(
                () => PhotoTestData.Prompts(root).ListAsync(PhotoPrompts.SceneKind));

            Assert.Contains(".comfyui/prompts", error.Message);
        }
        finally
        {
            PhotoTestData.TryDelete(root);
        }
    }
}
