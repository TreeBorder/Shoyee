using Shoyee.Storage.Abstractions;

namespace Shoyee.Handlers.Tests.TestSupport;

/// <summary>内存状态仓库（只测读取）。</summary>
internal sealed class FakeStateRepository : IStateRepository
{
    public List<StateRecord> Items { get; } = [];

    public Task<IReadOnlyList<StateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StateRecord>>(Items);

    public Task<StateRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(state => state.Key == key));

    public Task<bool> SetAsync(string key, string value, bool byAgent = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<StateRecord> UpsertAsync(StateRecord record, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task SeedIfEmptyAsync(IReadOnlyList<StateRecord> defaults, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
