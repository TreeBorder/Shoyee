using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;

namespace Shoyee.Channels.Attachments;

/// <summary>
/// 入站附件落地：<c>{workspace}/inbox/YYYY/MM/&lt;时间戳&gt;_&lt;短id&gt;&lt;后缀&gt;</c>。
///
/// 与 <c>assets/images</c>（模型产出）分开：收件不参与工具路径黑名单，也不该被当成她的作品。
/// 文件名只取最后一段并去掉路径分隔符，避免渠道传来的名字把文件写到目录外。
/// </summary>
public sealed class InboundAttachmentStore : IInboundAttachmentStore
{
    private readonly string _root;
    private readonly TimeProvider _clock;

    /// <summary>创建附件仓库。</summary>
    /// <param name="config">工作区配置（取根目录）。</param>
    /// <param name="clock">时间源（测试可注入）。</param>
    public InboundAttachmentStore(WorkspaceConfig config, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _root = Path.Combine(config.Root, "inbox");
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<InboundAttachment> SaveAsync(
        string fileName,
        string? mime,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().ToLocalTime();
        var directory = Path.Combine(_root, now.ToString("yyyy"), now.ToString("MM"));
        Directory.CreateDirectory(directory);

        var safeName = SanitizeName(fileName);
        var extension = Path.GetExtension(safeName);
        var path = Path.Combine(
            directory,
            $"{now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}{extension}");
        await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);

        var resolvedMime = string.IsNullOrWhiteSpace(mime) ? GuessMime(extension) : mime;
        return new InboundAttachment(
            safeName,
            resolvedMime,
            path,
            data.Length,
            Classify(resolvedMime, extension));
    }

    /// <summary>只留文件名本身：渠道可能给绝对路径或带 <c>..</c> 的名字。</summary>
    private static string SanitizeName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "attachment" : name;
    }

    private static InboundAttachmentKind Classify(string mime, string extension)
    {
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return InboundAttachmentKind.Image;
        }

        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return InboundAttachmentKind.Audio;
        }

        return extension.ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" => InboundAttachmentKind.Image,
            ".mp3" or ".wav" or ".m4a" or ".ogg" or ".silk" => InboundAttachmentKind.Audio,
            _ => InboundAttachmentKind.File,
        };
    }

    private static string GuessMime(string extension) => extension.ToLowerInvariant() switch
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
        ".pdf" => "application/pdf",
        ".txt" or ".md" => "text/plain",
        _ => "application/octet-stream",
    };
}
