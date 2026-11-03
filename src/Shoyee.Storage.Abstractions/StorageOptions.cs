namespace Shoyee.Storage.Abstractions;

/// <summary>存储连接配置。数据库选择只在这里体现，业务代码不感知。</summary>
/// <param name="Provider">提供方。</param>
/// <param name="ConnectionString">连接串（SQLite 为 <c>Data Source=...</c>）。</param>
public sealed record StorageOptions(StorageProvider Provider, string ConnectionString)
{
    /// <summary>SQLite 单文件。</summary>
    public static StorageOptions ForSqlite(string databasePath)
        => new(StorageProvider.Sqlite, $"Data Source={databasePath}");

    /// <summary>解析配置里的提供方名（大小写不敏感）。</summary>
    public static StorageProvider ParseProvider(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "postgres" or "postgresql" or "pgsql" or "npgsql" => StorageProvider.Postgres,
        _ => StorageProvider.Sqlite,
    };
}
