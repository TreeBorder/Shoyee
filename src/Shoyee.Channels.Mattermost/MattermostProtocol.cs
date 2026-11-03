using System.Buffers;
using System.Text.Json;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Channels.Mattermost;

/// <summary>
/// Mattermost 协议纯函数层（无 IO，可单测）：<c>posted</c> 事件解析、门控判定、回投地址构造与拆分。
///
/// 回投地址（<see cref="InboundMessage.Target"/>）= 频道 + 可选 thread 根帖：
/// 频道帖 <c>mattermost:channel:{channel_id}:{thread}</c>（thread = root_id ?? post_id，顶层帖自为根）；
/// 私聊 <c>mattermost:dm:{channel_id}</c>，私聊里带 thread 回复再追加 <c>:{root_id}</c>。
/// 这个串对上层不透明，只有本适配器解析。
/// </summary>
public static class MattermostProtocol
{
    /// <summary>单帖正文上限（服务端 16383，取保守值）。</summary>
    public const int MaxPostLength = 4000;

    private static readonly SearchValues<char> SentenceEnd = SearchValues.Create("\n。！？.!?");

    /// <summary>解析一条 WS 事件 JSON；非 <c>posted</c> 或形状不符返回 null。</summary>
    public static MattermostPost? ParsePostedEvent(string eventJson, string selfUsername, string selfUserId)
    {
        using var outer = JsonDocument.Parse(eventJson);
        var root = outer.RootElement;
        if (root.TryGetProperty("event", out var ev) && ev.GetString() != "posted")
        {
            return null;
        }

        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("post", out var postElement))
        {
            return null;
        }

        // data.post 是 JSON 字符串（双重编码），channel_type / sender_name 在 data 层
        using var postDoc = JsonDocument.Parse(postElement.GetString() ?? string.Empty);
        var post = postDoc.RootElement;
        var channelType = data.TryGetProperty("channel_type", out var ct) ? ct.GetString() ?? string.Empty : string.Empty;
        var message = post.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;

        var mentioned = message.Contains("@" + selfUsername, StringComparison.OrdinalIgnoreCase);
        if (!mentioned && post.TryGetProperty("props", out var props)
            && props.ValueKind == JsonValueKind.Object
            && props.TryGetProperty("mentions", out var mentions)
            && mentions.ValueKind == JsonValueKind.Array)
        {
            mentioned = mentions.EnumerateArray().Any(x => x.GetString() == selfUserId);
        }

        return new MattermostPost(
            Id: post.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            UserId: post.TryGetProperty("user_id", out var uid) ? uid.GetString() ?? string.Empty : string.Empty,
            ChannelId: post.TryGetProperty("channel_id", out var cid) ? cid.GetString() ?? string.Empty : string.Empty,
            Message: message,
            RootId: post.TryGetProperty("root_id", out var rid) ? rid.GetString() ?? string.Empty : string.Empty,
            Type: post.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty,
            ChannelType: channelType,
            Mentioned: mentioned,
            FileIds: ParseFileIds(post));
    }

    /// <summary>附件文件 id：<c>file_ids</c> 数组优先，回落 <c>metadata.files[].id</c>（不同服务端版本给的层不一样）。</summary>
    private static IReadOnlyList<string> ParseFileIds(JsonElement post)
    {
        var ids = new List<string>();
        if (post.TryGetProperty("file_ids", out var fileIds) && fileIds.ValueKind == JsonValueKind.Array)
        {
            ids.AddRange(fileIds.EnumerateArray()
                .Select(static x => x.GetString() ?? string.Empty)
                .Where(static x => x.Length > 0));
        }

        if (ids.Count == 0
            && post.TryGetProperty("metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty("files", out var files)
            && files.ValueKind == JsonValueKind.Array)
        {
            ids.AddRange(files.EnumerateArray()
                .Where(static f => f.ValueKind == JsonValueKind.Object
                    && f.TryGetProperty("id", out var fid)
                    && fid.ValueKind == JsonValueKind.String)
                .Select(static f => f.GetProperty("id").GetString() ?? string.Empty)
                .Where(static x => x.Length > 0));
        }

        return ids;
    }

    /// <summary>门控：系统消息/自发消息/白名单外/频道内未 @（私聊免）都不处理。</summary>
    /// <param name="post">帖子。</param>
    /// <param name="selfUserId">bot 自己的用户 id。</param>
    /// <param name="allowedUsers">允许的用户 id；空 = 不限。</param>
    /// <param name="requireMention">频道内是否要求 @bot（私聊免）。</param>
    public static bool ShouldHandle(
        MattermostPost post,
        string selfUserId,
        IReadOnlyList<string>? allowedUsers,
        bool requireMention)
    {
        if (post.Type.Length > 0 || post.UserId == selfUserId)
        {
            return false;
        }

        if (allowedUsers is { Count: > 0 } && !allowedUsers.Contains(post.UserId))
        {
            return false;
        }

        var isDm = post.ChannelType == "D";
        return isDm || !requireMention || post.Mentioned;
    }

    /// <summary>帖子 → 回投地址。</summary>
    public static string BuildTarget(MattermostPost post)
    {
        if (post.ChannelType == "D")
        {
            return post.RootId.Length == 0
                ? $"mattermost:dm:{post.ChannelId}"
                : $"mattermost:dm:{post.ChannelId}:{post.RootId}";
        }

        var thread = post.RootId.Length == 0 ? post.Id : post.RootId;
        return $"mattermost:channel:{post.ChannelId}:{thread}";
    }

    /// <summary>解析回投地址里的频道与可选 thread 根帖。</summary>
    public static (string ChannelId, string? ThreadId) ParseTarget(string target)
    {
        var parts = target.Split(':');
        if (parts.Length < 3)
        {
            throw new FormatException($"非法 Mattermost 回投地址：{target}");
        }

        return (parts[2], parts.Length >= 4 && parts[3].Length > 0 ? parts[3] : null);
    }

    /// <summary>剥掉开头的 @bot 提及，得到干净的用户输入。</summary>
    public static string StripMention(string message, string selfUsername)
    {
        var trimmed = message.TrimStart();
        var mention = "@" + selfUsername;
        return trimmed.StartsWith(mention, StringComparison.OrdinalIgnoreCase)
            ? trimmed[mention.Length..].TrimStart()
            : message;
    }

    /// <summary>按平台上限切分长文本（优先段落/换行边界）。</summary>
    public static IReadOnlyList<string> SplitPost(string text)
    {
        if (text.Length <= MaxPostLength)
        {
            return [text];
        }

        var chunks = new List<string>();
        var remaining = text.AsSpan();
        while (remaining.Length > 0)
        {
            if (remaining.Length <= MaxPostLength)
            {
                chunks.Add(remaining.ToString());
                break;
            }

            var cut = remaining[..MaxPostLength].LastIndexOfAny(SentenceEnd);
            cut = cut < MaxPostLength / 2 ? MaxPostLength : cut + 1;
            chunks.Add(remaining[..cut].TrimEnd().ToString());
            remaining = remaining[cut..].TrimStart();
        }

        return chunks;
    }
}
