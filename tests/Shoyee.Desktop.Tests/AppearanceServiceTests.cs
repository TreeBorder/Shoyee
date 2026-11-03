using Shoyee.Desktop.Theming;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>外观设置的持久化。</summary>
public sealed class AppearanceServiceTests
{
    [Fact]
    public void SavesAndLoadsThemeAccentAndWidth()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoyee-appearance-{Guid.NewGuid():N}");
        try
        {
            var service = new AppearanceService(directory);
            var saved = new AppearanceSettings(
                ThemeMode.Light,
                AccentColor.Teal,
                ChatFillWidth: false,
                PortraitScale: 0.85,
                DisplayName: "小义",
                Tagline: "测试用",
                WorkspacePath: "/tmp/shoyee",
                SidebarCollapsed: true);
            service.Save(saved);

            var loaded = service.Load();
            Assert.Equal(ThemeMode.Light, loaded.Theme);
            Assert.Equal(AccentColor.Teal, loaded.Accent);
            Assert.False(loaded.ChatFillWidth);
            Assert.Equal(0.85, loaded.PortraitScale);
            Assert.Equal("小义", loaded.DisplayName);
            Assert.Equal("测试用", loaded.Tagline);
            Assert.Equal("/tmp/shoyee", loaded.WorkspacePath);
            Assert.True(loaded.SidebarCollapsed);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SavesAndLoadsPreviewCommand()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoyee-appearance-{Guid.NewGuid():N}");
        try
        {
            var service = new AppearanceService(directory);
            service.Save(new AppearanceSettings(PreviewCommand: "open -R \"{path}\""));

            Assert.Equal("open -R \"{path}\"", service.Load().PreviewCommand);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void MigratesOld128kDefaultTo1M()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoyee-appearance-{Guid.NewGuid():N}");
        try
        {
            var service = new AppearanceService(directory);
            service.Save(new AppearanceSettings(
                MainModel: new ModelProfileSettings(ContextLength: 128_000),
                SettingsVersion: 0));

            Assert.Equal(1_000_000, service.Load().Models!.Entries[0].ContextLength);

            // 版本 2 之后，用户主动填 128k 不再被迁移
            service.Save(new AppearanceSettings(
                MainModel: new ModelProfileSettings(ContextLength: 128_000),
                SettingsVersion: 2));
            var loaded = service.Load();
            Assert.Equal(128_000, loaded.Models!.Entries[0].ContextLength);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void MigratesLegacyProfilesIntoModelCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoyee-appearance-{Guid.NewGuid():N}");
        try
        {
            var service = new AppearanceService(directory);
            var shared = new ModelProfileSettings("https://api.example.com", "shared", "EXAMPLE_KEY", 64_000);
            var vision = new ModelProfileSettings("https://api.example.com", "vision-model", "EXAMPLE_KEY", 32_000);
            service.Save(new AppearanceSettings(
                MainModel: shared,
                SmallModel: shared,
                VisionModel: vision,
                SettingsVersion: 2));

            var catalog = service.Load().Models!;
            Assert.Equal(2, catalog.Entries.Count);
            var chat = catalog.Entries.Single(entry => entry.Id == catalog.ChatEntryId);
            Assert.Equal("shared", chat.Model);
            Assert.Equal([ModelScene.Chat, ModelScene.Small], chat.Scenes);
            Assert.Equal("vision-model", catalog.Entries.Single(entry => entry.Id == catalog.VisionEntryId).Model);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SavesChatDisplayAndModelCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoyee-appearance-{Guid.NewGuid():N}");
        try
        {
            var service = new AppearanceService(directory);
            var catalog = new ModelCatalogSettings(
                [
                    new ModelEntrySettings(
                        "chat",
                        "主模型",
                        "https://api.example.com",
                        "example-chat",
                        "EXAMPLE_KEY",
                        64_000,
                        [ModelScene.Chat, ModelScene.Small]),
                    new ModelEntrySettings(
                        "vision",
                        "视觉",
                        "https://api.example.com",
                        "example-vision",
                        "EXAMPLE_KEY",
                        32_000,
                        [ModelScene.Vision]),
                ],
                "chat",
                "chat",
                "vision");
            service.Save(new AppearanceSettings(
                ShowToolCalls: false,
                ShowReasoning: true,
                SendOnEnter: true,
                Models: catalog,
                WideBubbles: true));

            var loaded = service.Load();
            Assert.False(loaded.ShowToolCalls);
            Assert.True(loaded.ShowReasoning);
            Assert.True(loaded.SendOnEnter);
            Assert.True(loaded.WideBubbles);
            Assert.NotNull(loaded.Models);
            Assert.Equal(2, loaded.Models!.Entries.Count);
            Assert.Equal("chat", loaded.Models.ChatEntryId);
            Assert.Equal("chat", loaded.Models.SmallEntryId);
            Assert.Equal("vision", loaded.Models.VisionEntryId);
            Assert.Equal("example-vision", loaded.Models.Entries[1].Model);
            Assert.Null(loaded.MainModel);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
