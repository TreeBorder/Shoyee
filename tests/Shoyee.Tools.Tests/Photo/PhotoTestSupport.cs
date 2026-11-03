using Shoyee.Contracts.Options;
using Shoyee.Photo;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Tools.Tests.Photo;

internal static class PhotoTestSupport
{
    public const string Lane = "desktop:roleplay";

    public static string NewRoot() => Path.Combine(
        Path.GetTempPath(),
        "shoyee-tools-photo-" + Guid.NewGuid().ToString("N"));

    public static WorkspaceConfig Config(string root, IReadOnlyList<string>? denied = null) => new(
        root,
        new ModelProfiles(
            new ModelEndpoint("http://localhost", "m", "KEY"),
            new ModelEndpoint("http://localhost", "s", "KEY")),
        new ChannelAccounts(),
        new ProactiveOptions(new HeartbeatOptions()),
        new ToolOptions(DeniedPaths: denied));

    public static void WriteVocabulary(string root)
    {
        var directory = Path.Combine(root, ".comfyui", "prompts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "scene.json"),
            """{"bedroom":{"displayName":"卧室","environmentCategory":"indoor","prompt":["bedroom"]}}""");
        File.WriteAllText(Path.Combine(directory, "outfit.json"),
            """{"casual":{"displayName":"休闲","prompt":["outfit"]},"dress":{"displayName":"裙装","prompt":["dress"]}}""");
        File.WriteAllText(Path.Combine(directory, "action.json"),
            """{"standing":{"displayName":"站立","prompt":["action"]}}""");
        File.WriteAllText(Path.Combine(directory, "angle.json"),
            """{"front":{"displayName":"正面","prompt":["angle"]}}""");
        File.WriteAllText(Path.Combine(directory, "character.json"),
            """{"xiaoyi":{"displayName":"筱义","prompt":["character"]}}""");
        File.WriteAllText(Path.Combine(directory, "quality.json"),
            """{"default":{"prompt":["quality"]}}""");
        File.WriteAllText(Path.Combine(directory, "environment.json"),
            """{"default":{"prompt":["light"]}}""");
    }

    public static PhotoPrompts Prompts(string root)
        => new(Path.Combine(root, ".comfyui", "prompts"));

    public static PhotoService Service(string root, IPhotoProvider? provider = null)
        => new(
            Config(root),
            new PhotoComposer(Prompts(root), "xiaoyi"),
            provider ?? new FakeProvider());

    public static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    internal sealed class FakeProvider(bool failing = false) : IPhotoProvider
    {
        public string Name => "fake";

        public bool Configured => true;

        public PhotoRequest? LastRequest { get; private set; }

        public Task<PhotoResult> GenerateAsync(PhotoRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return failing
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(new PhotoResult([new PhotoImage("image/png", [1, 2])]));
        }
    }

    internal sealed class FakeDelivery : IPhotoDelivery
    {
        private readonly TaskCompletionSource<PhotoDelivery> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PhotoDelivery> First => _first.Task;

        public Task DeliverAsync(PhotoDelivery delivery, CancellationToken cancellationToken = default)
        {
            _first.TrySetResult(delivery);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeStates : IStateRepository
    {
        private readonly Dictionary<string, StateRecord> _items = new(StringComparer.Ordinal);

        public FakeStates(params StateRecord[] records)
        {
            foreach (var record in records)
            {
                _items[record.Key] = record;
            }
        }

        public Task<IReadOnlyList<StateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StateRecord>>(_items.Values.ToArray());

        public Task<StateRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.TryGetValue(key, out var record) ? record : null);

        public Task<bool> SetAsync(string key, string value, bool byAgent = false, CancellationToken cancellationToken = default)
        {
            if (!_items.TryGetValue(key, out var record) || (byAgent && !record.AgentEditable))
            {
                return Task.FromResult(false);
            }

            _items[key] = record with { Value = value, UpdatedAt = DateTimeOffset.UtcNow };
            return Task.FromResult(true);
        }

        public Task<StateRecord> UpsertAsync(StateRecord record, CancellationToken cancellationToken = default)
        {
            _items[record.Key] = record;
            return Task.FromResult(record);
        }

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Remove(key));

        public Task SeedIfEmptyAsync(IReadOnlyList<StateRecord> defaults, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
