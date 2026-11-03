namespace Shoyee.Storage.Sqlite.Infrastructure;

/// <summary>存储路径（DI 单例，避免各处拼库文件路径）。</summary>
/// <param name="DatabasePath">库文件绝对路径。</param>
public sealed record StoragePaths(string DatabasePath);
