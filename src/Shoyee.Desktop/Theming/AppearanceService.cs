using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Shoyee.Desktop.Theming;

/// <summary>
/// 外观设置的读写与应用：颜色模式（深/浅/跟随系统）+ 强调色 + 会话宽度。
///
/// 全部只影响窗口；daemon 的工作区配置不掺这些。文件在应用数据根目录的 <c>desktop.json</c>。
/// </summary>
public sealed class AppearanceService
{
    /// <summary>desktop.json 的当前版本；版本 2 把旧的 128k 默认上下文迁移成 1M，版本 3 换成模型列表 + 场景绑定。</summary>
    private const int CurrentSettingsVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsPath;
    private AppearanceSettings _current = new();

    /// <summary>应用数据根目录（头像导入到这里，跟 desktop.json 同级）。</summary>
    public string AppDataRoot { get; }

    /// <summary>按应用数据根目录创建设置服务。</summary>
    /// <param name="appDataRoot">应用数据根目录（绝对路径）。</param>
    public AppearanceService(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        AppDataRoot = appDataRoot;
        _settingsPath = Path.Combine(appDataRoot, "desktop.json");

        if (Application.Current is { } app)
        {
            // 跟随系统时，系统明暗变化要重算整套配色（强调色深浅档 + 中性底色）
            app.ActualThemeVariantChanged += (_, _) => ApplyPalette(_current.Accent);
        }
    }

    /// <summary>读取设置；文件不存在/损坏时回落默认（暗色 + 藕粉 + 铺满）。</summary>
    public AppearanceSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return EmptySettings();
            }

            var text = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(text, JsonOptions);
            if (settings is null)
            {
                return EmptySettings();
            }

            var normalized = Migrate(settings with
            {
                Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : ThemeMode.Dark,
                Accent = Enum.IsDefined(settings.Accent) ? settings.Accent : AccentColor.Rose,
                PortraitScale = Math.Clamp(settings.PortraitScale, 0.5, 1.5),
            });

            // 旧文件只有三个档位，没有模型列表；按端点去重后换成「列表 + 场景绑定」。
            var fromLegacy = normalized.Models is null;
            return normalized with
            {
                Models = NormalizeCatalog(fromLegacy ? CatalogFromLegacy(normalized) : normalized.Models!),
                MainModel = fromLegacy ? normalized.MainModel : null,
                SmallModel = fromLegacy ? normalized.SmallModel : null,
                VisionModel = fromLegacy ? normalized.VisionModel : null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return EmptySettings();
        }
    }

    private static AppearanceSettings EmptySettings() => new(
        Models: ModelCatalogSettings.Default,
        SettingsVersion: CurrentSettingsVersion);

    /// <summary>应用设置：先切颜色模式，再把强调色写进资源。</summary>
    /// <param name="settings">外观设置。</param>
    public void Apply(AppearanceSettings settings)
    {
        _current = settings;
        ApplyToApplication(settings);
    }

    /// <summary>不经过持久化服务，直接把设置应用到当前 Application（测试/预览用）。</summary>
    /// <param name="settings">外观设置。</param>
    public static void ApplyToApplication(AppearanceSettings settings)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = settings.Theme switch
        {
            ThemeMode.Dark => ThemeVariant.Dark,
            ThemeMode.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        // 刚切主题时 ActualThemeVariant 可能还没刷新，显式传一次明暗
        bool? dark = settings.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => null,
        };
        ApplyPalette(settings.Accent, dark);
    }

    /// <summary>保存设置；失败只忽略，不拦界面。</summary>
    /// <param name="settings">外观设置。</param>
    public void Save(AppearanceSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 偏好写不进去不影响使用
        }
    }

    private static AppearanceSettings Migrate(AppearanceSettings settings)
    {
        // 128k 是上一版桌面预览写下的默认值，不是用户主动改的；升到 1M 默认。
        // 版本 2 之后的文件保留用户值，不再覆盖。
        if (settings.SettingsVersion < 2)
        {
            settings = settings with
            {
                MainModel = BumpOldDefault(settings.MainModel),
                SmallModel = BumpOldDefault(settings.SmallModel),
                VisionModel = BumpOldDefault(settings.VisionModel),
            };
        }

        return settings with { SettingsVersion = CurrentSettingsVersion };
    }

    private static ModelProfileSettings? BumpOldDefault(ModelProfileSettings? profile) =>
        profile is { ContextLength: 128_000 }
            ? profile with { ContextLength = 1_000_000 }
            : profile;

    private static ModelProfileSettings Normalize(ModelProfileSettings? profile)
    {
        var fallback = new ModelProfileSettings();
        if (profile is null)
        {
            return fallback;
        }

        return profile with
        {
            BaseUrl = string.IsNullOrWhiteSpace(profile.BaseUrl) ? fallback.BaseUrl : profile.BaseUrl.Trim(),
            Model = string.IsNullOrWhiteSpace(profile.Model) ? fallback.Model : profile.Model.Trim(),
            KeyEnv = string.IsNullOrWhiteSpace(profile.KeyEnv) ? fallback.KeyEnv : profile.KeyEnv.Trim(),
            ContextLength = Math.Clamp(profile.ContextLength, 4_096, 4_000_000),
        };
    }

    /// <summary>把旧版三个档位换算成「模型列表 + 场景绑定」，相同端点只留一条。</summary>
    private static ModelCatalogSettings CatalogFromLegacy(AppearanceSettings settings)
    {
        if (settings.MainModel is null && settings.SmallModel is null && settings.VisionModel is null)
        {
            return ModelCatalogSettings.Default;
        }

        var source = new (ModelScene Scene, ModelProfileSettings Profile)[]
        {
            (ModelScene.Chat, Normalize(settings.MainModel)),
            (ModelScene.Small, Normalize(settings.SmallModel)),
            (ModelScene.Vision, Normalize(settings.VisionModel)),
        };

        var entries = new List<ModelEntrySettings>();
        var scenesByKey = new Dictionary<string, List<ModelScene>>(StringComparer.Ordinal);
        var idsByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var sceneEntryId = new Dictionary<ModelScene, string>();

        foreach (var (scene, profile) in source)
        {
            var key = $"{profile.BaseUrl}\n{profile.Model}\n{profile.KeyEnv}\n{profile.ContextLength}";
            if (!scenesByKey.TryGetValue(key, out var scenes))
            {
                scenes = [];
                scenesByKey[key] = scenes;
                var id = $"model-{entries.Count + 1}";
                idsByKey[key] = id;
                entries.Add(new ModelEntrySettings(
                    id,
                    scene switch
                    {
                        ModelScene.Chat => "主模型",
                        ModelScene.Small => "小模型",
                        _ => "视觉模型",
                    },
                    profile.BaseUrl,
                    profile.Model,
                    profile.KeyEnv,
                    profile.ContextLength,
                    scenes));
            }

            scenes.Add(scene);
            sceneEntryId[scene] = idsByKey[key];
        }

        return new ModelCatalogSettings(
            entries,
            sceneEntryId.GetValueOrDefault(ModelScene.Chat),
            sceneEntryId.GetValueOrDefault(ModelScene.Small),
            sceneEntryId.GetValueOrDefault(ModelScene.Vision));
    }

    private static ModelCatalogSettings NormalizeCatalog(ModelCatalogSettings catalog)
    {
        var entries = new List<ModelEntrySettings>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in catalog.Entries ?? [])
        {
            if (entry is null)
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
            if (!seenIds.Add(id))
            {
                continue;
            }

            var scenes = (entry.Scenes ?? [])
                .Where(Enum.IsDefined)
                .Distinct()
                .ToList();
            if (scenes.Count == 0)
            {
                scenes.Add(ModelScene.Chat);
            }

            var fallback = new ModelProfileSettings();
            entries.Add(new ModelEntrySettings(
                id,
                string.IsNullOrWhiteSpace(entry.Name) ? entry.Model : entry.Name.Trim(),
                string.IsNullOrWhiteSpace(entry.BaseUrl) ? fallback.BaseUrl : entry.BaseUrl.Trim(),
                string.IsNullOrWhiteSpace(entry.Model) ? fallback.Model : entry.Model.Trim(),
                string.IsNullOrWhiteSpace(entry.KeyEnv) ? fallback.KeyEnv : entry.KeyEnv.Trim(),
                Math.Clamp(entry.ContextLength, 4_096, 4_000_000),
                scenes));
        }

        if (entries.Count == 0)
        {
            return ModelCatalogSettings.Default;
        }

        return new ModelCatalogSettings(
            entries,
            ResolveBinding(catalog.ChatEntryId, entries),
            ResolveBinding(catalog.SmallEntryId, entries),
            ResolveBinding(catalog.VisionEntryId, entries));
    }

    private static string? ResolveBinding(string? id, List<ModelEntrySettings> entries) =>
        string.IsNullOrWhiteSpace(id) ? null : entries.FirstOrDefault(entry => entry.Id == id)?.Id;

    private static void ApplyPalette(AccentColor accent, bool? darkOverride = null)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var dark = darkOverride ?? app.ActualThemeVariant != ThemeVariant.Light;
        var palette = AccentPalettes.Get(accent);
        var surface = SurfacePalettes.Get(accent, dark);
        var resources = app.Resources;

        // 强调色本体
        resources["AccentBrush"] = new SolidColorBrush(palette.Base(dark));
        resources["AccentHoverBrush"] = new SolidColorBrush(palette.Hover(dark));
        resources["AccentSoftBrush"] = new SolidColorBrush(palette.Soft(dark));
        resources["AccentBorderBrush"] = new SolidColorBrush(palette.Border(dark));
        resources["OnAccentBrush"] = new SolidColorBrush(palette.OnAccent(dark));
        resources["AccentColor"] = palette.Base(dark);
        resources["AccentGlowColor"] = palette.Glow(dark);
        resources["AccentGlowFadeColor"] = palette.GlowFade(dark);

        // 中性底色跟着强调色色相走。写进 Application.Resources 会盖住 App.axaml 里的
        // 静态 ThemeDictionaries（ResourceDictionary 先查自身条目，再查主题字典），
        // 所以这里必须每次都把全部键重写一遍，切明暗时才不会残留上一套颜色。
        resources["BgBrush"] = new SolidColorBrush(surface.Bg);
        resources["BgRaisedBrush"] = new SolidColorBrush(surface.BgRaised);
        resources["PanelBrush"] = new SolidColorBrush(surface.Panel);
        resources["CardBrush"] = new SolidColorBrush(surface.Card);
        resources["CardHoverBrush"] = new SolidColorBrush(surface.CardHover);
        resources["BorderBrush"] = new SolidColorBrush(surface.Border);
        resources["BorderSoftBrush"] = new SolidColorBrush(surface.BorderSoft);
        resources["TextBrush"] = new SolidColorBrush(surface.Text);
        resources["TextDimBrush"] = new SolidColorBrush(surface.TextDim);
        resources["TextMutedBrush"] = new SolidColorBrush(surface.TextMuted);
        resources["ComposerBrush"] = new SolidColorBrush(surface.Composer);
        resources["PreviewABrush"] = new SolidColorBrush(surface.PreviewA);
        resources["PreviewBBrush"] = new SolidColorBrush(surface.PreviewB);
        resources["PreviewAColor"] = surface.PreviewA;
        resources["PreviewBColor"] = surface.PreviewB;
        resources["DialogueBackdropBrush"] = new SolidColorBrush(surface.DialogueBackdrop);
        resources["StageWashBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(surface.StageStart, 0),
                new GradientStop(surface.StageMid, 0.55),
                new GradientStop(surface.StageEnd, 1),
            ],
        };

        // Fluent 控件（复选框、开关、滑块、输入聚焦框、下拉选中项）默认还是系统蓝，
        // 不接管的话界面里会同时出现两种强调色。
        var baseColor = palette.Base(dark);
        resources["SystemAccentColor"] = baseColor;
        resources["SystemAccentColorLight1"] = ShiftLightness(baseColor, 0.08);
        resources["SystemAccentColorLight2"] = ShiftLightness(baseColor, 0.16);
        resources["SystemAccentColorLight3"] = ShiftLightness(baseColor, 0.24);
        resources["SystemAccentColorDark1"] = ShiftLightness(baseColor, -0.08);
        resources["SystemAccentColorDark2"] = ShiftLightness(baseColor, -0.16);
        resources["SystemAccentColorDark3"] = ShiftLightness(baseColor, -0.24);
    }

    /// <summary>按亮度增减派生 Fluent 的 Light/Dark 档；色相与饱和度保持不变。</summary>
    private static Color ShiftLightness(Color color, double delta)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = Math.Clamp((max + min) / 2 + delta, 0, 1);
        var deltaRgb = max - min;

        if (deltaRgb <= double.Epsilon)
        {
            var gray = (byte)Math.Round(lightness * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var saturation = lightness is > 0 and < 1
            ? Math.Clamp(deltaRgb / (1 - Math.Abs(2 * lightness - 1)), 0, 1)
            : 0;
        var hue = max == r
            ? (g - b) / deltaRgb + (g < b ? 6 : 0)
            : max == g
                ? (b - r) / deltaRgb + 2
                : (r - g) / deltaRgb + 4;
        hue *= 60;

        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var offset = lightness - chroma / 2;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };

        return Color.FromRgb(Channel(red + offset), Channel(green + offset), Channel(blue + offset));
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
