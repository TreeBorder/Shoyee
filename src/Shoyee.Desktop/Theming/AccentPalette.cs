using Avalonia.Media;

namespace Shoyee.Desktop.Theming;

/// <summary>一组强调色：暗色模式用偏亮的一档，亮色模式用偏深的一档，保证对比度。</summary>
/// <param name="Dark">暗色模式基色。</param>
/// <param name="Light">亮色模式基色。</param>
public sealed record AccentPalette(Color Dark, Color Light)
{
    /// <summary>按当前明暗取基色。</summary>
    /// <param name="dark">是否暗色模式。</param>
    public Color Base(bool dark) => dark ? Dark : Light;

    /// <summary>悬停色（暗色再亮一点，亮色再深一点）。</summary>
    /// <param name="dark">是否暗色模式。</param>
    public Color Hover(bool dark)
    {
        var color = Base(dark);
        return dark
            ? Color.FromArgb(color.A, Lighten(color.R), Lighten(color.G), Lighten(color.B))
            : Color.FromArgb(color.A, Darken(color.R), Darken(color.G), Darken(color.B));
    }

    /// <summary>在基色上叠文字的颜色（按亮度选深/浅）。</summary>
    /// <param name="dark">是否暗色模式。</param>
    public Color OnAccent(bool dark)
    {
        var color = Base(dark);
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
        return luminance > 0.58 ? Color.Parse("#241018") : Colors.White;
    }

    /// <summary>半透明底色（选中态、浅标签）。</summary>
    /// <param name="dark">是否暗色模式。</param>
    public Color Soft(bool dark) => WithAlpha(Base(dark), dark ? (byte)0x40 : (byte)0x30);

    /// <summary>半透明描边。</summary>
    /// <param name="dark">是否暗色模式。</param>
    public Color Border(bool dark) => WithAlpha(Base(dark), dark ? (byte)0x80 : (byte)0x66);

    /// <summary>背景光晕起始色。</summary>
    /// <param name="dark">是否暗色模式。</param>
    public Color Glow(bool dark) => WithAlpha(Base(dark), dark ? (byte)0x59 : (byte)0x40);

    /// <summary>背景光晕消失色。</summary>
    /// <param name="dark">是否暗色模式。</param>
    public Color GlowFade(bool dark) => WithAlpha(Base(dark), 0);

    private static byte Lighten(byte channel) => (byte)Math.Min(255, channel + 22);

    private static byte Darken(byte channel) => (byte)Math.Max(0, channel - 26);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}

/// <summary>强调色预设表。</summary>
public static class AccentPalettes
{
    /// <summary>取预设。</summary>
    /// <param name="accent">强调色。</param>
    public static AccentPalette Get(AccentColor accent) => accent switch
    {
        AccentColor.Blue => new AccentPalette(Color.Parse("#8FB8E8"), Color.Parse("#3E6FA8")),
        AccentColor.Teal => new AccentPalette(Color.Parse("#8FD0C6"), Color.Parse("#2F7E74")),
        AccentColor.Amber => new AccentPalette(Color.Parse("#E8C27E"), Color.Parse("#A8762C")),
        AccentColor.Violet => new AccentPalette(Color.Parse("#B9A0E8"), Color.Parse("#6B4FA8")),
        AccentColor.Slate => new AccentPalette(Color.Parse("#A9B4C4"), Color.Parse("#4C5A6E")),
        _ => new AccentPalette(Color.Parse("#E8A2B4"), Color.Parse("#B85F7C")),
    };
}
