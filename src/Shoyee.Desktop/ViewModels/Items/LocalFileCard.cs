using Shoyee.Contracts.Domain;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>
/// 本地文件 → 文件气泡/附件卡片的展示数据（文件名、大小、类别）。
///
/// 桌面和 daemon 同机，路径就是真源：这里只负责把后缀翻成展示类别，
/// 不读内容、不上传。<see cref="AttachmentItem.Path"/> 供点击打开用。
/// </summary>
public static class LocalFileCard
{
    /// <summary>为一个本地路径构造卡片数据；文件已不存在时仍返回，标记为「已移动或删除」。</summary>
    /// <param name="path">本地绝对路径。</param>
    /// <param name="label">链接文字（优先作为展示名）。</param>
    public static AttachmentItem FromPath(string path, string? label = null)
        => new()
        {
            Kind = Classify(path),
            FileName = string.IsNullOrWhiteSpace(label) ? Path.GetFileName(path) : label.Trim(),
            Meta = Describe(path),
            Path = path,
        };

    /// <summary>按后缀判断展示类别。</summary>
    /// <param name="path">文件路径。</param>
    public static AttachmentKind Classify(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".heic" or ".bmp" => AttachmentKind.Image,
        ".mp3" or ".wav" or ".m4a" or ".ogg" or ".silk" or ".flac" => AttachmentKind.Audio,
        ".mp4" or ".mov" or ".m4v" or ".webm" => AttachmentKind.Video,
        _ => AttachmentKind.File,
    };

    private static string Describe(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return "文件已移动或删除";
        }

        return info.Length switch
        {
            >= 1024 * 1024 => $"{info.Length / 1024d / 1024d:0.#} MB",
            >= 1024 => $"{info.Length / 1024d:0.#} KB",
            _ => $"{info.Length} B",
        };
    }
}
