namespace Shoyee.Voice;

/// <summary>
/// 音频容器与 MIME 的对照，供各供应商拼请求 / 落盘用（不做转码）。
/// </summary>
public static class AudioFormats
{
    /// <summary>按扩展名给 MIME；不认识的返回 <c>null</c>。</summary>
    /// <param name="pathOrExtension">文件路径或扩展名（含不含点都行）。</param>
    public static string? MimeFor(string pathOrExtension)
    {
        var extension = Path.GetExtension(pathOrExtension);
        if (string.IsNullOrEmpty(extension))
        {
            extension = pathOrExtension.StartsWith('.') ? pathOrExtension : "." + pathOrExtension;
        }

        return extension.ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" or ".aac" => "audio/mp4",
            ".ogg" or ".opus" => "audio/ogg",
            ".flac" => "audio/flac",
            ".aiff" or ".aif" => "audio/aiff",
            ".caf" => "audio/x-caf",
            ".amr" => "audio/amr",
            ".silk" => "audio/silk",
            _ => null,
        };
    }

    /// <summary>扩展名是否在给定集合里（不区分大小写）。</summary>
    /// <param name="path">文件路径。</param>
    /// <param name="extensions">允许的扩展名（含点）。</param>
    public static bool HasExtension(string path, IReadOnlySet<string> extensions)
        => extensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// 粗算音频时长（毫秒）：只解析 WAV（RIFF 块），其他容器返回 null。
    ///
    /// 桌面画语音气泡的时长用；算不出时客户端可以退化到「不显示时长」，
    /// 所以这里不引第三方解码库。
    /// </summary>
    /// <param name="data">音频字节。</param>
    /// <param name="mime">MIME（wav 才解析）。</param>
    public static int? DurationMilliseconds(byte[]? data, string? mime)
    {
        if (data is null || data.Length < 44
            || !string.Equals(mime, "audio/wav", StringComparison.OrdinalIgnoreCase)
            || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F')
        {
            return null;
        }

        int? byteRate = null;
        var offset = 12;
        while (offset + 8 <= data.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var size = BitConverter.ToInt32(data, offset + 4);
            if (size < 0 || offset + 8 + size > data.Length)
            {
                // 有些写入方把奇数长度块补齐；长度越界就当头部损坏，别硬读
                size = Math.Min(size < 0 ? 0 : size, data.Length - offset - 8);
            }

            var body = offset + 8;
            if (id == "fmt " && size >= 12)
            {
                byteRate = BitConverter.ToInt32(data, body + 8);
            }
            else if (id == "data" && byteRate is > 0)
            {
                return (int)Math.Round(size * 1000.0 / byteRate.Value);
            }

            offset = body + size + (size % 2);
        }

        return null;
    }
}
