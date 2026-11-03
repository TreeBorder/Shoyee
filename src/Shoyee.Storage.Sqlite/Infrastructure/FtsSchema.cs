using Microsoft.Data.Sqlite;

namespace Shoyee.Storage.Sqlite.Infrastructure;

/// <summary>
/// FTS5 虚表的建表与方言选择。EF 不管虚表，启动时单独确保存在。
/// 优先 trigram（中文子串友好）；不可用时退回 unicode61。
/// </summary>
public static class FtsSchema
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> EnsuredPaths = new(StringComparer.Ordinal);

    /// <summary>最近一次建表是否启用了 trigram 分词。</summary>
    public static bool Trigram { get; private set; } = true;

    /// <summary>幂等建表（按库文件路径，每个进程每个库只做一次）。</summary>
    public static void Ensure(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        lock (Gate)
        {
            if (EnsuredPaths.Contains(fullPath))
            {
                return;
            }

            using var connection = SqliteConnectionFactory.Open(fullPath);
            if (!TryCreate(connection, "trigram"))
            {
                TryCreate(connection, "unicode61");
                Trigram = false;
            }

            EnsuredPaths.Add(fullPath);
        }
    }

    private static bool TryCreate(SqliteConnection connection, string tokenizer)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                $"CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(content, message_id UNINDEXED, tokenize='{tokenizer}');";
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }
}
