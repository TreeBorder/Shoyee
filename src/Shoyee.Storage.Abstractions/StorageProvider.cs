namespace Shoyee.Storage.Abstractions;

/// <summary>存储提供方。</summary>
public enum StorageProvider
{
    /// <summary>SQLite（单文件，默认）。</summary>
    Sqlite,

    /// <summary>PostgreSQL（预留，实现后启用）。</summary>
    Postgres,
}
