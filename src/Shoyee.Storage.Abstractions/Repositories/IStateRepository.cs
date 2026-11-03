namespace Shoyee.Storage.Abstractions;

/// <summary>通用状态仓库（键值 + 说明），新增状态不需要改代码或迁移。</summary>
public interface IStateRepository
{
    /// <summary>全部状态。</summary>
    Task<IReadOnlyList<StateRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>按 key 取一条。</summary>
    Task<StateRecord?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>改值；<paramref name="byAgent"/> 为 true 时受 <c>AgentEditable</c> 限制。返回是否改到。</summary>
    Task<bool> SetAsync(string key, string value, bool byAgent = false, CancellationToken cancellationToken = default);

    /// <summary>新增或按 key 覆盖（设置界面/初始化用，不受 AgentEditable 限制）。</summary>
    Task<StateRecord> UpsertAsync(StateRecord record, CancellationToken cancellationToken = default);

    /// <summary>删除；返回是否删到。</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>表为空时写入默认状态（幂等）。</summary>
    Task SeedIfEmptyAsync(IReadOnlyList<StateRecord> defaults, CancellationToken cancellationToken = default);
}
