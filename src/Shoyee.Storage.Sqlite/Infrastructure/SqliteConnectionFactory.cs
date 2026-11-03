using Microsoft.Data.Sqlite;

namespace Shoyee.Storage.Sqlite.Infrastructure;

/// <summary>SQLite 连接工厂（WAL + busy_timeout），供 FTS5 与原生 SQL 用。</summary>
public static class SqliteConnectionFactory
{
    /// <summary>打开一条已启用 busy_timeout 的连接（调用方负责释放）。</summary>
    public static SqliteConnection Open(string databasePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA journal_mode = WAL;";
        cmd.ExecuteNonQuery();
        return connection;
    }
}
