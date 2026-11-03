using Avalonia.Media.Imaging;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>加载气泡里的 Markdown 图片：本地路径、<c>file://</c> 与 <c>http(s)</c> 都直接读。</summary>
internal static class BubbleImageLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>解码一张图片；失败返回 null，由气泡显示占位。</summary>
    /// <param name="source">Markdown 图片括号里的地址。</param>
    /// <param name="width">解码宽度（默认 900px；附件缩略图给更小的值）。</param>
    /// <returns>按指定宽度解码的位图；读不到或格式不支持时为 null。</returns>
    public static Bitmap? Load(string source, int width = 900)
    {
        try
        {
            using var stream = Open(source);
            return stream is null ? null : Bitmap.DecodeToWidth(stream, Math.Max(32, width));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Stream? Open(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            switch (uri.Scheme)
            {
                case "http":
                case "https":
                    return new MemoryStream(Http.GetByteArrayAsync(uri).GetAwaiter().GetResult());
                case "file":
                    return File.Exists(uri.LocalPath) ? File.OpenRead(uri.LocalPath) : null;
            }
        }

        var path = source.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), source[1..].TrimStart('/'))
            : source;
        return File.Exists(path) ? File.OpenRead(path) : null;
    }
}
