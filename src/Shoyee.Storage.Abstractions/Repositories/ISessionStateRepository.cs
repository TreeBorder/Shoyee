namespace Shoyee.Storage.Abstractions;

/// <summary>框架序列化的会话状态（原文存取，不改写）。</summary>
public interface ISessionStateRepository
{
    /// <summary>按存储键取 JSON；没有返回 null。</summary>
    Task<string?> GetAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>整写保存（存在即覆盖）。</summary>
    Task SaveAsync(string storageKey, string stateJson, DateTimeOffset? at = null, CancellationToken cancellationToken = default);

    /// <summary>删除。</summary>
    Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
