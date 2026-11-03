using Shoyee.Contracts.Dtos;
using Shoyee.Memory.Index;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Memory.Tests.Index;

public sealed class IndexStoreTests
{
    [Fact]
    public void Render_Empty_ReturnsNull()
    {
        Assert.Null(IndexStore.Render([]));
    }

    [Fact]
    public void Render_IncludesKindValueAndNotes()
    {
        var now = DateTimeOffset.UtcNow;
        var text = IndexStore.Render(
        [
            new IndexEntry(1, "shoyee", "Shoyee", "path", "/tmp/shoyee", "all", null, "项目", true, now, now),
            new IndexEntry(2, "akasha", "Akasha", "path", "/tmp/akasha", "work", null, null, false, now, now),
        ]);

        Assert.Contains("- Shoyee（path）｜/tmp/shoyee｜项目", text);
        Assert.Contains("- Akasha（path）｜/tmp/akasha", text);
    }

    [Fact]
    public async Task Find_IsCaseInsensitive()
    {
        var repo = new FakeIndexRepository(
        [
            new IndexEntry(1, "Shoyee", "Shoyee", "path", "/tmp/shoyee", "all", null, null, false,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        ]);
        var store = new IndexStore(repo);

        Assert.NotNull(await store.FindAsync("shoyee"));
        Assert.Null(await store.FindAsync("missing"));
    }

    private sealed class FakeIndexRepository(IReadOnlyList<IndexEntry> entries) : IIndexRepository
    {
        public Task<IReadOnlyList<IndexEntry>> ListAsync(string? scope = null, string? query = null, bool enabledOnly = false, CancellationToken cancellationToken = default)
            => Task.FromResult(entries);

        public Task<IndexEntry> UpsertAsync(IndexEntry entry, CancellationToken cancellationToken = default)
            => Task.FromResult(entry);

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
