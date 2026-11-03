namespace Shoyee.Storage.Abstractions;

/// <summary>启动期存储初始化（迁移 + 检索索引建表）。</summary>
public interface IStorageInitializer
{
    /// <summary>幂等初始化。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
