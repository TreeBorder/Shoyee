using Microsoft.Data.Sqlite;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Converters;
using Shoyee.Storage;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Sqlite.Infrastructure;

namespace Shoyee.Storage.Sqlite.Repositories;

/// <summary>
/// 会话/轮次检索：FTS5 trigram + LIKE 兜底。查的是消息投影（两种模式共表）。
/// trigram 至少 3 个字符，「打卡」这类两字词 MATCH 不命中，调用方不需要关心查询长度。
/// </summary>
public sealed class SearchRepository : ISearchRepository
{
    private readonly string _databasePath;

    /// <summary>创建检索实现。</summary>
    public SearchRepository(StoragePaths paths)
    {
        _databasePath = paths.DatabasePath;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> SearchSessionsAsync(
        string query,
        SessionSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return [];
        }

        FtsSchema.Ensure(_databasePath);
        await using var connection = SqliteConnectionFactory.Open(_databasePath);

        if (trimmed.Length >= 3)
        {
            var hits = await SearchMatchAsync(connection, trimmed, filter, cancellationToken);
            if (hits.Count > 0)
            {
                return hits;
            }
        }

        return await SearchLikeAsync(connection, trimmed, filter, cancellationToken);
    }

    private static async Task<List<SearchHit>> SearchMatchAsync(
        SqliteConnection connection,
        string query,
        SessionSearchFilter filter,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT m.id, m.session_id, m.lane_id, m.round_no, m.role, m.content, m.created_at,
                   s.title, l.mode, l.channel
            FROM messages_fts
            JOIN messages m ON m.id = CAST(messages_fts.message_id AS INTEGER)
            LEFT JOIN sessions s ON s.id = m.session_id
            LEFT JOIN lanes l ON l.id = m.lane_id
            WHERE messages_fts MATCH $q AND m.content <> ''
            """ + FilterSql(filter) + """
             ORDER BY bm25(messages_fts) LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$q", Quote(query));
        cmd.Parameters.AddWithValue("$limit", filter.Limit);
        AddFilters(cmd, filter);

        try
        {
            return await ReadHitsAsync(cmd, query, cancellationToken);
        }
        catch (SqliteException)
        {
            // 查询语法异常（引号/运算符）直接走 LIKE，不让检索炸掉
            return [];
        }
    }

    private static async Task<List<SearchHit>> SearchLikeAsync(
        SqliteConnection connection,
        string query,
        SessionSearchFilter filter,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT m.id, m.session_id, m.lane_id, m.round_no, m.role, m.content, m.created_at,
                   s.title, l.mode, l.channel
            FROM messages m
            LEFT JOIN sessions s ON s.id = m.session_id
            LEFT JOIN lanes l ON l.id = m.lane_id
            WHERE m.content <> '' AND m.content LIKE $like ESCAPE '\'
            """ + FilterSql(filter) + """
             ORDER BY m.id DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$like", "%" + EscapeLike(query) + "%");
        cmd.Parameters.AddWithValue("$limit", filter.Limit);
        AddFilters(cmd, filter);

        return await ReadHitsAsync(cmd, query, cancellationToken);
    }

    private static string FilterSql(SessionSearchFilter filter)
    {
        var sql = string.Empty;
        if (filter.Mode is not null)
        {
            sql += " AND l.mode = $mode";
        }

        if (filter.LaneId is not null)
        {
            sql += " AND m.lane_id = $lane";
        }

        return sql;
    }

    private static void AddFilters(SqliteCommand cmd, SessionSearchFilter filter)
    {
        if (filter.Mode is { } mode)
        {
            cmd.Parameters.AddWithValue("$mode", mode.ToString());
        }

        if (filter.LaneId is { } lane)
        {
            cmd.Parameters.AddWithValue("$lane", lane);
        }
    }

    private static async Task<List<SearchHit>> ReadHitsAsync(
        SqliteCommand cmd,
        string query,
        CancellationToken cancellationToken)
    {
        var hits = new List<SearchHit>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var content = reader.GetString(5);
            hits.Add(new SearchHit(
                SessionId: reader.IsDBNull(1) ? null : reader.GetString(1),
                SessionTitle: reader.IsDBNull(7) ? null : reader.GetString(7),
                LaneId: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                MessageId: reader.GetInt64(0),
                RoundNo: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Snippet: SearchWindow.Cut(content, query),
                Role: ParseRole(reader.GetString(4)),
                Mode: reader.IsDBNull(8) ? Mode.Work : ParseEnum(reader.GetString(8), Mode.Work),
                Origin: reader.IsDBNull(9) ? LaneChannel.Desktop : ParseEnum(reader.GetString(9), LaneChannel.Desktop),
                CreatedAt: new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero)));
        }

        return hits;
    }

    private static MessageRole ParseRole(string value) => value.ToLowerInvariant() switch
    {
        "user" => MessageRole.User,
        "assistant" => MessageRole.Assistant,
        "system" => MessageRole.System,
        "tool" => MessageRole.Tool,
        _ => MessageRole.Unknown,
    };

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static string Quote(string query) => "\"" + query.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EscapeLike(string query)
        => query.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
}
