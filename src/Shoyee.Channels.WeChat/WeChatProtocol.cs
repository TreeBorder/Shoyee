using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shoyee.Channels.WeChat;

/// <summary>微信入站消息（iLink <c>getupdates</c> 消息的规范化视图）。</summary>
/// <param name="MessageId">消息 id（去重键）。</param>
/// <param name="FromUserId">发送者（私聊的会话 id）。</param>
/// <param name="ToUserId">接收者（bot 账号）。</param>
/// <param name="GroupId">群 id；空 = 私聊。</param>
/// <param name="ContextToken">会话续接令牌（每个 peer 一个，回信必须回带）。</param>
/// <param name="Text">正文（多 item 换行拼接；引用消息拍平；平台语音转写标注来源）。</param>
/// <param name="Media">媒体 item 引用（下载落盘由适配器做）。</param>
public sealed record WeChatInbound(
    string MessageId,
    string FromUserId,
    string ToUserId,
    string? GroupId,
    string? ContextToken,
    string Text,
    IReadOnlyList<WeChatMediaRef>? Media = null);

/// <summary>
/// 微信协议纯函数层（无 IO，可单测）：消息解析、回投地址、长文切分、内容指纹。
///
/// iLink 的 <c>root_id</c>/<c>parent_id</c>/<c>session_id</c> 实测均为空 → 没有 thread，
/// 会话粒度 = 私聊/群。
/// </summary>
public static class WeChatProtocol
{
    /// <summary>单条消息字符上限（iLink 约 2048 处自行分片，取 2000 保守值）。</summary>
    public const int MaxMessageLength = 2000;

    /// <summary>超过此长度主动切分（给 iLink 自分片留余量）。</summary>
    public const int SplitThreshold = 1800;

    private static readonly SearchValues<char> SentenceEnd = SearchValues.Create("\n。！？.!?");

    /// <summary>解析一条 <c>getupdates</c> 消息；形状不符返回 null。</summary>
    /// <param name="msg">消息 JSON 对象。</param>
    public static WeChatInbound? ParseMessage(JsonElement msg)
    {
        var messageId = msg.TryGetProperty("message_id", out var mid) ? mid.ToString() : string.Empty;
        var from = msg.TryGetProperty("from_user_id", out var f) ? f.GetString() ?? string.Empty : string.Empty;
        if (messageId.Length == 0 || from.Length == 0)
        {
            return null;
        }

        string? groupId = null;
        foreach (var key in new[] { "group_id", "room_id", "chat_room_id" })
        {
            if (msg.TryGetProperty(key, out var g) && g.GetString() is { Length: > 0 } gid)
            {
                groupId = gid;
                break;
            }
        }

        var texts = new List<string>();
        var media = new List<WeChatMediaRef>();
        if (msg.TryGetProperty("item_list", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) && type.GetInt32() == 1
                    && item.TryGetProperty("text_item", out var textItem)
                    && textItem.TryGetProperty("text", out var text)
                    && text.GetString() is { Length: > 0 } value)
                {
                    // 引用/回复拍平成前缀（iLink 的 ref_msg 没有被引消息 id，做不了 thread）
                    var prefix = item.TryGetProperty("ref_msg", out var refMsg)
                        && refMsg.TryGetProperty("title", out var title)
                        && title.GetString() is { Length: > 0 } refTitle
                            ? $"[引用: {refTitle}]\n"
                            : string.Empty;
                    texts.Add(prefix + value);
                }
                else if (ParseMediaItem(item) is { } mediaRef)
                {
                    // 平台的语音转写只当参考文本；原始音频照收进附件
                    if (mediaRef.Kind == WeChatMediaKind.Voice && mediaRef.VoiceText is { Length: > 0 } voiceText)
                    {
                        texts.Add($"[微信语音转写] {voiceText}");
                    }

                    media.Add(mediaRef);
                }
            }
        }

        return new WeChatInbound(
            MessageId: messageId,
            FromUserId: from,
            ToUserId: msg.TryGetProperty("to_user_id", out var t) ? t.GetString() ?? string.Empty : string.Empty,
            GroupId: groupId,
            ContextToken: msg.TryGetProperty("context_token", out var ctx) ? ctx.GetString() : null,
            Text: string.Join('\n', texts),
            Media: media);
    }

    /// <summary>解析一个媒体 item（图片/语音/文件/视频）；非媒体或形状不符返回 null。</summary>
    private static WeChatMediaRef? ParseMediaItem(JsonElement item)
    {
        if (!item.TryGetProperty("type", out var type))
        {
            return null;
        }

        WeChatMediaKind? kind = type.GetInt32() switch
        {
            2 => WeChatMediaKind.Image,
            3 => WeChatMediaKind.Voice,
            4 => WeChatMediaKind.File,
            5 => WeChatMediaKind.Video,
            _ => null,
        };
        if (kind is null)
        {
            return null;
        }

        var payloadKey = kind switch
        {
            WeChatMediaKind.Image => "image_item",
            WeChatMediaKind.Voice => "voice_item",
            WeChatMediaKind.Video => "video_item",
            _ => "file_item",
        };
        if (!item.TryGetProperty(payloadKey, out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? encryptedQuery = null, fullUrl = null, aesKeyB64 = null;
        if (payload.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
        {
            encryptedQuery = media.TryGetProperty("encrypt_query_param", out var q) ? q.GetString() : null;
            fullUrl = media.TryGetProperty("full_url", out var u) ? u.GetString() : null;
            aesKeyB64 = media.TryGetProperty("aes_key", out var k) ? k.GetString() : null;
        }

        if (string.IsNullOrEmpty(encryptedQuery) && string.IsNullOrEmpty(fullUrl))
        {
            return null; // 没有下载线索的 item 不是可用媒体
        }

        return new WeChatMediaRef(
            Kind: kind.Value,
            EncryptedQueryParam: encryptedQuery,
            FullUrl: fullUrl,
            AesKeyB64: aesKeyB64,
            AesKeyHex: payload.TryGetProperty("aeskey", out var hex) ? hex.GetString() : null,
            FileName: payload.TryGetProperty("file_name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            VoiceText: payload.TryGetProperty("text", out var vtext) ? vtext.GetString() : null);
    }

    /// <summary>回投地址：私聊 <c>wechat:dm:{user}</c>，群 <c>wechat:group:{group}</c>（无 thread）。</summary>
    public static string BuildTarget(WeChatInbound message)
        => message.GroupId is { Length: > 0 } groupId
            ? $"wechat:group:{groupId}"
            : $"wechat:dm:{message.FromUserId}";

    /// <summary>解析回投地址里的发送目标（群发群、私聊发人）。</summary>
    public static (string Target, bool IsGroup) ParseTarget(string target)
    {
        var parts = target.Split(':');
        if (parts.Length < 3)
        {
            throw new FormatException($"非法微信回投地址：{target}");
        }

        return (parts[2], parts[1] == "group");
    }

    /// <summary>
    /// 内容指纹（二级去重）：上游会用**新的 message_id** 重发同内容，
    /// 只按 id 去重会重复跑一轮。
    /// </summary>
    public static string ContentDedupeKey(string senderId, string text)
        => "content:" + senderId + ":" + Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>按 <see cref="SplitThreshold"/> 切分长文本（优先段落/句末边界）。</summary>
    public static IReadOnlyList<string> SplitText(string text)
    {
        if (text.Length <= SplitThreshold)
        {
            return [text];
        }

        var chunks = new List<string>();
        var remaining = text.AsSpan();
        while (remaining.Length > 0)
        {
            if (remaining.Length <= SplitThreshold)
            {
                chunks.Add(remaining.ToString());
                break;
            }

            var cut = remaining[..SplitThreshold].LastIndexOfAny(SentenceEnd);
            cut = cut < SplitThreshold / 2 ? SplitThreshold : cut + 1;
            chunks.Add(remaining[..cut].TrimEnd().ToString());
            remaining = remaining[cut..].TrimStart();
        }

        return chunks;
    }
}
