using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Tools.Media;

/// <summary>
/// 本地文件 → <see cref="MessageAttachment"/>：按扩展名猜 MIME 与展示类别。
///
/// 出站附件的路径来自工具自己写出的文件（或 <c>send_file</c> 给的现有文件），
/// 所以不需要像入站那样信任渠道的 MIME——文件在我们自己的盘上，按后缀判断就够。
/// 猜不出来时按「文件卡片」处理，仍可下载/预览。
/// </summary>
public static class AttachmentFactory
{
    /// <summary>为一个已存在的本地文件构造附件；文件不存在时抛 <see cref="FileNotFoundException"/>。</summary>
    /// <param name="path">本地绝对路径。</param>
    /// <param name="mime">已知 MIME（工具产出时自带，比猜的准）；空 = 按后缀猜。</param>
    public static MessageAttachment FromFile(string path, string? mime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("附件文件不存在。", path);
        }

        var resolvedMime = string.IsNullOrWhiteSpace(mime) ? GuessMime(path) : mime;
        return new MessageAttachment(
            path,
            Path.GetFileName(path),
            resolvedMime,
            info.Length,
            Classify(resolvedMime, path));
    }

    /// <summary>按 MIME/后缀判断展示类别。</summary>
    /// <param name="mime">MIME。</param>
    /// <param name="path">文件名或路径（只在 MIME 判断不出时用）。</param>
    public static AttachmentKind Classify(string mime, string path)
    {
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentKind.Image;
        }

        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentKind.Audio;
        }

        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentKind.Video;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" => AttachmentKind.Image,
            ".mp3" or ".wav" or ".m4a" or ".ogg" or ".silk" or ".flac" => AttachmentKind.Audio,
            ".mp4" or ".mov" or ".m4v" or ".webm" => AttachmentKind.Video,
            _ => AttachmentKind.File,
        };
    }

    /// <summary>按后缀猜 MIME；猜不出给 <c>application/octet-stream</c>。</summary>
    /// <param name="path">文件路径。</param>
    public static string GuessMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".ogg" => "audio/ogg",
        ".silk" => "audio/silk",
        ".flac" => "audio/flac",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        ".pdf" => "application/pdf",
        ".md" => "text/markdown",
        ".txt" or ".log" or ".csv" => "text/plain",
        ".json" => "application/json",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };
}
