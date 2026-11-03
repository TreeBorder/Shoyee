using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>内存版 session_state 仓库。</summary>
internal sealed class InMemorySessionStateRepository : ISessionStateRepository
{
    public Dictionary<string, string> Rows { get; } = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string storageKey, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.TryGetValue(storageKey, out var json) ? json : null);

    public Task SaveAsync(
        string storageKey,
        string stateJson,
        DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        Rows[storageKey] = stateJson;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.Remove(storageKey));
}
