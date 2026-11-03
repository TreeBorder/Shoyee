using Shoyee.Desktop.Theming;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>强调色推导出的中性底色：不同强调色必须真的换掉背景色相。</summary>
public sealed class SurfacePaletteTests
{
    [Fact]
    public void DarkBackgroundFollowsAccentHue()
    {
        var rose = SurfacePalettes.Get(AccentColor.Rose, dark: true).Bg;
        var blue = SurfacePalettes.Get(AccentColor.Blue, dark: true).Bg;
        var amber = SurfacePalettes.Get(AccentColor.Amber, dark: true).Bg;

        Assert.NotEqual(rose, blue);
        Assert.True(blue.B > blue.R, $"雾蓝底色应偏蓝，实际 {blue}");
        Assert.True(amber.R > amber.B, $"暖金底色应偏暖，实际 {amber}");
    }

    [Fact]
    public void SimilarHuesStayDistinguishableBySaturation()
    {
        var blue = SurfacePalettes.Get(AccentColor.Blue, dark: true).Bg;
        var slate = SurfacePalettes.Get(AccentColor.Slate, dark: true).Bg;

        Assert.NotEqual(blue, slate);
    }

    [Fact]
    public void LightBackgroundFollowsAccentHue()
    {
        var rose = SurfacePalettes.Get(AccentColor.Rose, dark: false).Bg;
        var teal = SurfacePalettes.Get(AccentColor.Teal, dark: false).Bg;

        Assert.NotEqual(rose, teal);
        Assert.True(teal.G > teal.B, $"青绿亮色底应偏青，实际 {teal}");
    }

    [Fact]
    public void RoseDarkStaysPlumInsteadOfWine()
    {
        var rose = SurfacePalettes.Get(AccentColor.Rose, dark: true).Bg;

        // 粉色暗底如果按 345° 算会发红（R 远高于 B），掰到品红后应当接近紫色
        Assert.True(rose.B >= rose.G, $"粉色暗底应偏紫，实际 {rose}");
        Assert.True(rose.R - rose.B < 12, $"粉色暗底不应发红，实际 {rose}");
    }

    [Fact]
    public void TextStaysReadableInBothModes()
    {
        foreach (var accent in Enum.GetValues<AccentColor>())
        {
            var dark = SurfacePalettes.Get(accent, dark: true);
            var light = SurfacePalettes.Get(accent, dark: false);

            Assert.True(Brightness(dark.Text) - Brightness(dark.Bg) > 0.6);
            Assert.True(Brightness(light.Bg) - Brightness(light.Text) > 0.6);
        }
    }

    private static double Brightness(Avalonia.Media.Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
}
