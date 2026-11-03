using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Conversations;

/// <summary>从 Secretary 的真实数据目录导入工作会话。</summary>
internal static partial class SecretaryHistoryImporter
{
    /// <summary>按路径导入；支持项目根、state.db、sessions 目录或单个会话 JSON。</summary>
    /// <param name="path">Secretary 项目根 / state.db / sessions 目录 / 单个会话文件。</param>
    /// <returns>按最后活跃时间倒序的会话。</returns>
    public static IReadOnlyList<SessionItem> Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("请输入 Secretary 的路径。", nameof(path));
        }

        path = path.Trim();
        if (File.Exists(path))
        {
            return Path.GetFileName(path) == "state.db"
                ? ImportDatabase(path)
                : [ImportSessionFile(path)];
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"目录不存在：{path}");
        }

        var database = Path.Combine(path, "state.db");
        if (File.Exists(database))
        {
            return ImportDatabase(database);
        }

        var sessions = Path.Combine(path, "sessions");
        if (Directory.Exists(sessions))
        {
            return ImportSessionFiles(sessions);
        }

        if (Directory.GetFiles(path, "*.json").Length > 0)
        {
            return ImportSessionFiles(path);
        }

        throw new FileNotFoundException($"这个目录里没有 state.db 或 sessions：{path}");
    }

    private static IReadOnlyList<SessionItem> ImportDatabase(string databasePath)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        connection.Open();

        var sessions = new Dictionary<string, WorkRawSession>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "select id, session_key, title, archived, pinned, created_at, last_activity_at " +
                "from sessions order by last_activity_at desc";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                sessions[id] = new WorkRawSession
                {
                    Id = id,
                    LaneId = reader.GetString(1),
                    Title = reader.GetString(2),
                    Archived = reader.GetInt64(3) != 0,
                    Pinned = reader.GetInt64(4) != 0,
                    CreatedAt = Parse(reader.GetString(5)),
                    LastActivityAt = Parse(reader.GetString(6)),
                };
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "select session_id, role, content, message_json, created_at " +
                "from messages order by session_id, seq";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var sessionId = reader.GetString(0);
                if (!sessions.TryGetValue(sessionId, out var session)
                    || reader.GetString(1) is not ("user" or "assistant" or "tool"))
                {
                    continue;
                }

                session.Messages.Add(new WorkRawMessage
                {
                    Role = reader.GetString(1),
                    Content = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    MessageJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CreatedAt = Parse(reader.GetString(4)),
                });
            }
        }

        // 上轮 API 用量（缓存命中率用）；旧库没有这张表时静默跳过。
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "select session_id, last_input_tokens, last_cached_input_tokens from session_usage";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (sessions.TryGetValue(reader.GetString(0), out var session))
                {
                    session.LastInputTokens = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                    session.LastCachedInputTokens = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                }
            }
        }
        catch (SqliteException)
        {
            // 没有 session_usage 的旧库：缓存命中率显示为「—」
        }

        return sessions.Values
            .Where(session => session.Messages.Count > 0)
            .Select(session =>
            {
                (session.Source, session.External) = SourceOf(session.LaneId);
                session.Title = BuildTitle(
                    session.Messages.FirstOrDefault(message => message.Role == "user")?.Content ?? "",
                    session.Title);
                session.MessageCount = session.Messages.Count;
                return SecretaryTranscriptReader.ReadSession(session);
            })
            .OrderByDescending(session => session.CreatedAt)
            .ToList();
    }

    private static IReadOnlyList<SessionItem> ImportSessionFiles(string directory)
    {
        return Directory.GetFiles(directory, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(file =>
            {
                try
                {
                    return ImportSessionFile(file);
                }
                catch (Exception ex) when (ex is JsonException or IOException or FormatException)
                {
                    return null;
                }
            })
            .OfType<SessionItem>()
            .OrderByDescending(session => session.CreatedAt)
            .ToList();
    }

    private static SessionItem ImportSessionFile(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var root = document.RootElement;

        var messages = new List<WorkRawMessage>();
        if (root.TryGetProperty("stateBag", out var stateBag)
            && stateBag.TryGetProperty("InMemoryChatHistoryProvider", out var history)
            && history.TryGetProperty("messages", out var array)
            && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in array.EnumerateArray())
            {
                if (!message.TryGetProperty("role", out var role)
                    || role.GetString() is not ("user" or "assistant" or "tool"))
                {
                    continue;
                }

                var createdAt = ReadCreatedAt(message) ?? File.GetLastWriteTimeUtc(file);
                messages.Add(new WorkRawMessage
                {
                    Role = role.GetString()!,
                    Content = role.GetString() == "user" ? ReadContentsText(message) : "",
                    MessageJson = message.GetRawText(),
                    CreatedAt = createdAt,
                });
            }
        }

        if (messages.Count == 0)
        {
            throw new InvalidOperationException("会话里没有可导入的消息。");
        }

        var first = messages.FirstOrDefault(message => message.Role == "user");
        var title = BuildTitle(first?.Content ?? "");
        var folder = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";
        var id = Path.GetFileNameWithoutExtension(file);
        var source = folder.Equals("weixin", StringComparison.OrdinalIgnoreCase) ? "微信" : "桌面";
        var last = messages.Count > 0 ? messages[^1].CreatedAt : File.GetLastWriteTimeUtc(file);
        return SecretaryTranscriptReader.ReadSession(new WorkRawSession
        {
            Id = id,
            LaneId = $"import:{id}",
            Title = title,
            Source = source,
            External = source != "桌面",
            CreatedAt = messages.Count > 0 ? messages[0].CreatedAt : last,
            LastActivityAt = last,
            MessageCount = messages.Count,
            Messages = messages,
        });
    }

    private static string BuildTitle(string content, string fallback = "")
    {
        if (!string.IsNullOrWhiteSpace(fallback)
            && !fallback.StartsWith("会话 ", StringComparison.Ordinal))
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return "未命名会话";
        }

        var (body, _) = ConversationText.SplitUserText(content);
        var plain = UrlRegex().Replace(ConversationText.ToPlainText(body), "链接")
            .Replace('\n', ' ')
            .Trim();
        return plain.Length == 0 ? "未命名会话" : ConversationText.Clip(plain, 22);
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlRegex();

    private static (string Source, bool External) SourceOf(string sessionKey) =>
        sessionKey.StartsWith("mattermost", StringComparison.OrdinalIgnoreCase) ? ("Mattermost", true)
        : sessionKey.StartsWith("weixin", StringComparison.OrdinalIgnoreCase) ? ("微信", true)
        : ("桌面", false);

    private static string ReadContentsText(JsonElement message)
    {
        if (!message.TryGetProperty("contents", out var contents)
            || contents.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var text = new StringBuilder();
        foreach (var content in contents.EnumerateArray())
        {
            if (content.TryGetProperty("$type", out var kind) && kind.GetString() == "text")
            {
                text.Append(ConversationText.ReadText(content));
            }
        }

        return text.ToString();
    }

    private static DateTimeOffset? ReadCreatedAt(JsonElement message) =>
        message.TryGetProperty("createdAt", out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
