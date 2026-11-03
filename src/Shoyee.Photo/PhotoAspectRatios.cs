namespace Shoyee.Photo;

/// <summary>角色图比例与对应尺寸（与 Harem 的 SDXL 出图一致）。</summary>
public static class PhotoAspectRatios
{
    /// <summary>竖屏 2:3（默认）。</summary>
    public const string Portrait = "2:3";

    /// <summary>横屏 3:2。</summary>
    public const string Landscape = "3:2";

    /// <summary>方图 1:1。</summary>
    public const string Square = "1:1";

    /// <summary>解析比例；不认识的值回落到默认 2:3。</summary>
    /// <param name="aspectRatio">比例字符串。</param>
    public static string Normalize(string? aspectRatio) => aspectRatio?.Trim() switch
    {
        Landscape => Landscape,
        Square => Square,
        _ => Portrait,
    };

    /// <summary>比例 → 宽高。</summary>
    /// <param name="aspectRatio">比例字符串（先经 <see cref="Normalize"/>）。</param>
    public static (int Width, int Height) Resolve(string? aspectRatio) => Normalize(aspectRatio) switch
    {
        Landscape => (1216, 832),
        Square => (1024, 1024),
        _ => (832, 1216),
    };
}
