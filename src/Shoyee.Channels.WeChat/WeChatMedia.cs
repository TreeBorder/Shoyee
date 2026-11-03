using System.Security.Cryptography;
using System.Text;

namespace Shoyee.Channels.WeChat;

/// <summary>
/// 微信媒体 item 种类（<c>item_list</c> entry type；与 <c>getuploadurl</c> 的 <c>media_type</c> 是两套值：
/// <c>ITEM_IMAGE=2 ITEM_VOICE=3 ITEM_FILE=4 ITEM_VIDEO=5</c>）。
/// </summary>
public enum WeChatMediaKind
{
    /// <summary>图片（image_item）。</summary>
    Image = 2,

    /// <summary>语音（voice_item；出站一律按文件发——原生语音泡未验证）。</summary>
    Voice = 3,

    /// <summary>文件（file_item）。</summary>
    File = 4,

    /// <summary>视频（video_item）。</summary>
    Video = 5,
}

/// <summary>入站媒体 item 的引用（已解析、还没下载）。</summary>
/// <param name="Kind">item 种类。</param>
/// <param name="EncryptedQueryParam">加密下载参数（有则走 <c>{cdn}/download?encrypted_query_param=...</c>）。</param>
/// <param name="FullUrl">完整下载地址（仅允许 CDN 白名单 host，防 SSRF）。</param>
/// <param name="AesKeyB64"><c>media.aes_key</c>（base64；内容是 16 原始字节或 32 字符 hex 串）。</param>
/// <param name="AesKeyHex">image_item 旁挂的裸 hex <c>aeskey</c>（优先级更高）。</param>
/// <param name="FileName">文件名（file_item 才有）。</param>
/// <param name="VoiceText">平台 STT 转写（非中文不可靠，只当参考文本）。</param>
public sealed record WeChatMediaRef(
    WeChatMediaKind Kind,
    string? EncryptedQueryParam,
    string? FullUrl,
    string? AesKeyB64,
    string? AesKeyHex,
    string FileName,
    string? VoiceText);

/// <summary>微信媒体协议纯函数层（无 IO，可单测）：AES-128-ECB、出站 item 形状、类型分派、CDN 白名单。</summary>
public static class WeChatMedia
{
    /// <summary><c>getuploadurl</c> 的 media_type：图片。</summary>
    public const int UploadTypeImage = 1;

    /// <summary><c>getuploadurl</c> 的 media_type：视频。</summary>
    public const int UploadTypeVideo = 2;

    /// <summary><c>getuploadurl</c> 的 media_type：文件。</summary>
    public const int UploadTypeFile = 3;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v" };

    /// <summary>微信 CDN host 白名单（<c>full_url</c> 下载只认这些，防 SSRF）。</summary>
    private static readonly HashSet<string> CdnHostAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "novac2c.cdn.weixin.qq.com",
        "ilinkai.weixin.qq.com",
        "wx.qlogo.cn",
        "thirdwx.qlogo.cn",
        "res.wx.qq.com",
        "mmbiz.qpic.cn",
        "mmbiz.qlogo.cn",
    };

    /// <summary>按扩展名分派出站类型：图片/视频分派，其余（含音频）一律文件附件。</summary>
    public static (int UploadType, WeChatMediaKind Kind) ClassifyByFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ImageExtensions.Contains(ext))
        {
            return (UploadTypeImage, WeChatMediaKind.Image);
        }

        if (VideoExtensions.Contains(ext))
        {
            return (UploadTypeVideo, WeChatMediaKind.Video);
        }

        return (UploadTypeFile, WeChatMediaKind.File);
    }

    /// <summary>
    /// 出站媒体 item：<c>media</c> 块 = <c>{encrypt_query_param, aes_key, encrypt_type:1}</c> + 各类型附加字段。
    /// <paramref name="aesKeyForApi"/> 必须是 **base64(hex 字符串)**，不是 base64 原始字节（写错图片显示灰块）。
    /// </summary>
    public static Dictionary<string, object> BuildOutboundItem(
        WeChatMediaKind kind,
        string encryptedQueryParam,
        string aesKeyForApi,
        long ciphertextSize,
        long plaintextSize,
        string fileName,
        string rawFileMd5)
    {
        var media = new Dictionary<string, object>
        {
            ["encrypt_query_param"] = encryptedQueryParam,
            ["aes_key"] = aesKeyForApi,
            ["encrypt_type"] = 1,
        };

        return kind switch
        {
            WeChatMediaKind.Image => new Dictionary<string, object>
            {
                ["type"] = (int)kind,
                ["image_item"] = new Dictionary<string, object> { ["media"] = media, ["mid_size"] = ciphertextSize },
            },
            WeChatMediaKind.Video => new Dictionary<string, object>
            {
                ["type"] = (int)kind,
                ["video_item"] = new Dictionary<string, object>
                {
                    ["media"] = media,
                    ["video_size"] = ciphertextSize,
                    ["play_length"] = 0,
                    ["video_md5"] = rawFileMd5,
                },
            },
            WeChatMediaKind.Voice => new Dictionary<string, object>
            {
                ["type"] = (int)kind,
                ["voice_item"] = new Dictionary<string, object>
                {
                    ["media"] = media,
                    ["encode_type"] = 6,
                    ["bits_per_sample"] = 16,
                    ["sample_rate"] = 24000,
                    ["playtime"] = 0,
                },
            },
            _ => new Dictionary<string, object>
            {
                ["type"] = (int)WeChatMediaKind.File,
                ["file_item"] = new Dictionary<string, object>
                {
                    ["media"] = media,
                    ["file_name"] = fileName,
                    ["len"] = plaintextSize.ToString(),
                },
            },
        };
    }

    /// <summary>API 用的 aes_key 编码 = base64(hex 字符串)。</summary>
    public static string EncodeAesKeyForApi(byte[] aesKey)
        => Convert.ToBase64String(Encoding.ASCII.GetBytes(Convert.ToHexString(aesKey).ToLowerInvariant()));

    /// <summary>解析入站 <c>media.aes_key</c>（base64）：16 字节原始密钥，或 32 字符 hex 串。</summary>
    public static byte[] ParseAesKey(string aesKeyB64)
    {
        var decoded = Convert.FromBase64String(aesKeyB64);
        if (decoded.Length == 16)
        {
            return decoded;
        }

        if (decoded.Length == 32)
        {
            var text = Encoding.ASCII.GetString(decoded);
            if (text.All(static ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            {
                return Convert.FromHexString(text);
            }
        }

        throw new FormatException($"aes_key 格式无法识别（解码后 {decoded.Length} 字节）");
    }

    /// <summary>取入站媒体的 AES 密钥（裸 hex <c>aeskey</c> 优先）；没有可解析的密钥返回 null。</summary>
    public static byte[]? ResolveAesKey(WeChatMediaRef media)
    {
        try
        {
            if (!string.IsNullOrEmpty(media.AesKeyHex))
            {
                return Convert.FromHexString(media.AesKeyHex);
            }

            return string.IsNullOrEmpty(media.AesKeyB64) ? null : ParseAesKey(media.AesKeyB64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>AES-128-ECB + PKCS7 加密（整文件）。</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    /// <summary>AES-128-ECB 解密并剥 PKCS7 填充（填充不规范就原样返回块输出）。</summary>
    public static byte[] Decrypt(byte[] ciphertext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        using var decryptor = aes.CreateDecryptor();
        var padded = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        var padLen = padded.Length == 0 ? 0 : padded[^1];
        if (padLen is >= 1 and <= 16 && padded.Length >= padLen
            && padded.AsSpan(padded.Length - padLen).ToArray().All(b => b == padLen))
        {
            return padded[..^padLen];
        }

        return padded;
    }

    /// <summary><c>full_url</c> 是否可下载（仅 http/https 且 host 在白名单，防 SSRF）。</summary>
    public static bool IsAllowedCdnUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && CdnHostAllowlist.Contains(uri.Host);
}
