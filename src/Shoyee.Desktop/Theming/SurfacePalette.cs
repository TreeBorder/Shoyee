using Avalonia.Media;

namespace Shoyee.Desktop.Theming;

/// <summary>
/// 由强调色推导出的整套中性底色。
///
/// 只换 <c>Accent*</c> 时，页面底色仍是固定藕粉，视觉上几乎看不出换色；
/// 这里按强调色色相重新生成背景 / 卡片 / 边框 / 文字，暗色模式才会跟着变成暗蓝黑、暗青黑等。
/// </summary>
/// <param name="Bg">窗口底色。</param>
/// <param name="BgRaised">抬升区域（输入区、状态栏）。</param>
/// <param name="Panel">面板（弹层、侧栏）。</param>
/// <param name="Card">卡片。</param>
/// <param name="CardHover">卡片悬停 / 选中。</param>
/// <param name="Border">常规描边。</param>
/// <param name="BorderSoft">浅描边。</param>
/// <param name="Text">正文。</param>
/// <param name="TextDim">次要文字。</param>
/// <param name="TextMuted">弱化文字。</param>
/// <param name="Composer">输入框底色。</param>
/// <param name="PreviewA">预览块起始色。</param>
/// <param name="PreviewB">预览块结束色。</param>
/// <param name="StageStart">舞台渐变起始色。</param>
/// <param name="StageMid">舞台渐变中间色。</param>
/// <param name="StageEnd">舞台渐变结束色。</param>
/// <param name="DialogueBackdrop">对话浮层半透明底。</param>
public sealed record SurfacePalette(
    Color Bg,
    Color BgRaised,
    Color Panel,
    Color Card,
    Color CardHover,
    Color Border,
    Color BorderSoft,
    Color Text,
    Color TextDim,
    Color TextMuted,
    Color Composer,
    Color PreviewA,
    Color PreviewB,
    Color StageStart,
    Color StageMid,
    Color StageEnd,
    Color DialogueBackdrop);

/// <summary>按强调色 + 明暗生成中性底色表。</summary>
public static class SurfacePalettes
{
    /// <summary>取一组中性色。</summary>
    /// <param name="accent">强调色。</param>
    /// <param name="dark">是否暗色模式。</param>
    public static SurfacePalette Get(AccentColor accent, bool dark)
    {
        var baseColor = AccentPalettes.Get(accent).Base(dark);
        var (hue, saturation) = HueSaturation(baseColor);

        // 只取色相，饱和度压到中低，避免背景比强调色还抢眼；
        // 保留强调色自身饱和度的比例，石墨灰蓝才不会被算成和雾蓝一样。
        // 暗色底本来就接近黑，饱和度不拉高的话色相根本看不出来，所以暗色档比亮色档更饱和；
        // 但粉色饱和一高就整片发红，单独压回去，保持「粉黑」而不是「酒红」。
        var tint = Math.Clamp(saturation, 0.05, 0.75)
                   * (dark ? 0.75 : 0.90)
                   * TintScale(accent, dark);

        // 红色系色相（粉色在 HSL 里约 345°）压到暗底上只会读出「酒红」，
        // 往品红方向掰一点才是粉黑该有的紫调。
        var surfaceHue = accent == AccentColor.Rose && dark ? 320 : hue;

        if (dark)
        {
            var bg = FromHsl(surfaceHue, tint * 0.85, 0.095);
            var bgRaised = FromHsl(surfaceHue, tint * 0.85, 0.115);
            return new SurfacePalette(
                Bg: bg,
                BgRaised: bgRaised,
                Panel: FromHsl(surfaceHue, tint * 0.85, 0.135),
                Card: FromHsl(surfaceHue, tint * 0.85, 0.165),
                CardHover: FromHsl(surfaceHue, tint * 0.90, 0.205),
                Border: FromHsl(surfaceHue, tint * 0.90, 0.275),
                BorderSoft: FromHsl(surfaceHue, tint * 0.85, 0.215),
                Text: FromHsl(surfaceHue, tint * 0.45, 0.947),
                TextDim: FromHsl(surfaceHue, tint * 0.55, 0.700),
                TextMuted: FromHsl(surfaceHue, tint * 0.50, 0.505),
                Composer: bgRaised,
                PreviewA: FromHsl(surfaceHue, tint * 0.95, 0.240),
                PreviewB: FromHsl(surfaceHue, tint * 0.85, 0.170),
                StageStart: FromHsl(surfaceHue, tint * 0.80, 0.155),
                StageMid: FromHsl(surfaceHue, tint * 0.75, 0.125),
                StageEnd: FromHsl(surfaceHue, tint * 0.70, 0.090),
                DialogueBackdrop: WithAlpha(bgRaised, 0xEB));
        }

        var lightBg = FromHsl(hue, tint * 1.15, 0.955);
        return new SurfacePalette(
            Bg: lightBg,
            BgRaised: FromHsl(hue, tint * 1.20, 0.975),
            Panel: FromHsl(hue, tint * 0.85, 0.995),
            Card: FromHsl(hue, tint * 0.85, 0.995),
            CardHover: FromHsl(hue, tint * 1.25, 0.935),
            Border: FromHsl(hue, tint * 1.15, 0.870),
            BorderSoft: FromHsl(hue, tint * 1.15, 0.915),
            Text: FromHsl(hue, tint * 0.85, 0.145),
            TextDim: FromHsl(hue, tint * 0.80, 0.330),
            TextMuted: FromHsl(hue, tint * 0.70, 0.545),
            Composer: FromHsl(hue, tint * 0.85, 0.998),
            PreviewA: FromHsl(hue, tint * 1.30, 0.920),
            PreviewB: FromHsl(hue, tint * 1.20, 0.870),
            StageStart: FromHsl(hue, tint * 1.10, 0.970),
            StageMid: FromHsl(hue, tint * 1.10, 0.945),
            StageEnd: FromHsl(hue, tint * 1.10, 0.920),
            DialogueBackdrop: WithAlpha(FromHsl(hue, tint * 0.85, 0.995), 0xF2));
    }

    /// <summary>
    /// 按强调色微调中性底色的饱和度。粉色在暗底上一有饱和度就整片发红（像酒红/姨妈红），
    /// 暗色档只留一点点粉味；亮色档和其余颜色保持原样。
    /// </summary>
    private static double TintScale(AccentColor accent, bool dark) => accent switch
    {
        AccentColor.Rose when dark => 0.40,
        _ => 1.0,
    };

    private static (double Hue, double Saturation) HueSaturation(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta <= double.Epsilon)
        {
            return (0, 0);
        }

        var lightness = (max + min) / 2;
        var saturation = lightness > 0.5
            ? delta / (2 - max - min)
            : delta / (max + min);

        var hue = max == r
            ? (g - b) / delta + (g < b ? 6 : 0)
            : max == g
                ? (b - r) / delta + 2
                : (r - g) / delta + 4;

        return (hue * 60, saturation);
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);

        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var offset = lightness - chroma / 2;

        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };

        return Color.FromRgb(Channel(r + offset), Channel(g + offset), Channel(b + offset));
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
