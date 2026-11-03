using Shoyee.Contracts.Options;
using Shoyee.Photo;

namespace Shoyee.Photo.Tests;

internal static class PhotoTestData
{
    public static string NewRoot() => Path.Combine(
        Path.GetTempPath(),
        "shoyee-photo-" + Guid.NewGuid().ToString("N"));

    public static WorkspaceConfig Config(string root) => new(
        root,
        new ModelProfiles(
            new ModelEndpoint("http://localhost", "m", "KEY"),
            new ModelEndpoint("http://localhost", "s", "KEY")),
        new ChannelAccounts(),
        new ProactiveOptions(new HeartbeatOptions()),
        new ToolOptions());

    public static void WriteVocabulary(string root, bool withCharacter = true, bool withMorning = true)
    {
        var directory = Path.Combine(root, ".comfyui", "prompts");
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "quality.json"),
            """{"default":{"displayName":"默认质量","prompt":["quality"],"negative":["bad"]}}""");
        File.WriteAllText(Path.Combine(directory, "scene.json"),
            """
            {
              "bedroom": { "displayName": "卧室", "environmentCategory": "indoor", "prompt": ["bedroom"], "negative": ["bad"] },
              "studio": { "displayName": "棚", "environmentCategory": "studio", "prompt": ["studio"] }
            }
            """);
        File.WriteAllText(Path.Combine(directory, "environment.json"),
            """
            {
              "default": { "prompt": ["default-light"] },
              "indoor": { "prompt": ["indoor-light"] },
              "indoor_day": { "prompt": ["day-light"] },
              "indoor_morning": { "prompt": ["morning-light"] },
              "studio": { "prompt": ["studio-light"] }
            }
            """);
        if (!withMorning)
        {
            File.Delete(Path.Combine(directory, "environment.json"));
            File.WriteAllText(Path.Combine(directory, "environment.json"),
                """
                {
                  "default": { "prompt": ["default-light"] },
                  "indoor": { "prompt": ["indoor-light"] },
                  "indoor_day": { "prompt": ["day-light"] },
                  "studio": { "prompt": ["studio-light"] }
                }
                """);
        }

        File.WriteAllText(Path.Combine(directory, "outfit.json"),
            """{"casual":{"displayName":"休闲","prompt":["outfit"]}}""");
        File.WriteAllText(Path.Combine(directory, "action.json"),
            """{"standing":{"displayName":"站立","prompt":["action"]}}""");
        File.WriteAllText(Path.Combine(directory, "angle.json"),
            """{"front":{"displayName":"正面","prompt":["angle"]}}""");
        if (withCharacter)
        {
            File.WriteAllText(Path.Combine(directory, "character.json"),
                """{"xiaoyi":{"displayName":"筱义","prompt":["character"]}}""");
        }
    }

    public static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    public static PhotoPrompts Prompts(string root)
        => new(Path.Combine(root, ".comfyui", "prompts"));
}
