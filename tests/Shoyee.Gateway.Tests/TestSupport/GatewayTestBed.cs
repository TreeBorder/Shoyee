using Xunit;
using Shoyee.Client.Rpc;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Rpc;
using Shoyee.Gateway.Subscriptions;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Gateway.Tests.TestSupport;

/// <summary>起一个真实的 UDS Gateway（假领域服务 + 临时 socket），测试走真连接。</summary>
internal sealed class GatewayTestBed : IAsyncDisposable
{
    private GatewayTestBed(
        string root,
        ShoyeePaths paths,
        FakeLaneRepository lanes,
        FakeSessionService sessions,
        FakeTurnService turns,
        FakeSearchRepository search,
        FakeIndexRepository index,
        FakeStateRepository states,
        FakeUsageRepository usage,
        FakeCronRepository cron,
        FakeCronScheduler cronScheduler,
        WorkspaceConfig config,
        GatewayOptions options,
        EventHub hub,        ShutdownGate gate,
        DaemonControl control,
        GatewayHost host)
    {
        Root = root;
        Paths = paths;
        Lanes = lanes;
        Sessions = sessions;
        Turns = turns;
        Search = search;
        Index = index;
        States = states;
        Usage = usage;
        Cron = cron;
        CronScheduler = cronScheduler;
        Config = config;
        Options = options;
        Hub = hub;
        Gate = gate;
        Control = control;
        Host = host;
    }

    public string Root { get; }

    public ShoyeePaths Paths { get; }

    public FakeLaneRepository Lanes { get; }

    public FakeSessionService Sessions { get; }

    public FakeTurnService Turns { get; }

    public FakeSearchRepository Search { get; }

    public FakeIndexRepository Index { get; }

    public FakeStateRepository States { get; }

    public FakeUsageRepository Usage { get; }

    public FakeCronRepository Cron { get; }

    public FakeCronScheduler CronScheduler { get; }

    public WorkspaceConfig Config { get; }

    public GatewayOptions Options { get; }

    public EventHub Hub { get; }

    public ShutdownGate Gate { get; }

    public DaemonControl Control { get; }

    public GatewayHost Host { get; }

    public static async Task<GatewayTestBed> StartAsync(
        bool allowTestRole = false,
        INotificationPusher? notifications = null,
        ITtsService? tts = null,
        IAssetStore? assets = null,
        IConfigReloader? configReloader = null,
        IImageService? images = null,
        IToolCatalog? toolCatalog = null,
        IMcpControl? mcp = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "shoyee-gw-" + Guid.NewGuid().ToString("N")[..8]);
        var paths = new ShoyeePaths(root);
        paths.EnsureRunDirectory();

        var lanes = new FakeLaneRepository();
        lanes.Seed(
            new LaneRecord(
                LaneIds.DesktopRoleplay,
                LaneChannel.Desktop,
                Mode.Roleplay,
                "desktop",
                "桌面",
                null,
                null,
                null,
                DateTimeOffset.UnixEpoch),
            new LaneRecord(
                "mattermost:roleplay",
                LaneChannel.Mattermost,
                Mode.Roleplay,
                "mm-rp",
                "手机",
                null,
                null,
                null,
                DateTimeOffset.UnixEpoch),
            new LaneRecord(
                "mattermost:work",
                LaneChannel.Mattermost,
                Mode.Work,
                "http://mm",
                "工作",
                "ses_1",
                null,
                null,
                DateTimeOffset.UnixEpoch));

        var config = new WorkspaceConfig(
            root,
            new ModelProfiles(
                new ModelEndpoint("http://localhost:1/v1", "main-model", "MAIN_KEY"),
                new ModelEndpoint("http://localhost:1/v1", "small-model", "SMALL_KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());

        var options = new GatewayOptions
        {
            Version = RpcProtocol.Version,
            Workspace = root,
            SocketPath = paths.SocketPath,
            AppDataRoot = paths.AppDataRoot,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AllowTestRole = allowTestRole,
            StatusPushInterval = TimeSpan.FromMilliseconds(200),
            DatabaseInfo = () => new DbInfo(Path.Combine(root, "state.db"), 1024),
        };

        var sessions = new FakeSessionService();
        var turns = new FakeTurnService();
        var search = new FakeSearchRepository();
        var index = new FakeIndexRepository();
        var states = new FakeStateRepository();
        var usage = new FakeUsageRepository();
        var cron = new FakeCronRepository();
        var cronScheduler = new FakeCronScheduler();
        var hub = new EventHub();
        var services = new RpcServices(
            sessions, turns, search, index, lanes, states, usage, config, options,
            Cron: cron, CronScheduler: cronScheduler, Notifications: notifications, Tts: tts, Assets: assets,
            Images: images, ConfigReloader: configReloader, ToolCatalog: toolCatalog, Mcp: mcp, Events: hub);
        var gate = new ShutdownGate();
        var control = new DaemonControl();
        var dispatcher = new RpcDispatcher(services, gate, control);
        var host = new GatewayHost(services, hub, dispatcher, gate, options);
        await host.StartAsync(CancellationToken.None);

        return new GatewayTestBed(
            root,
            paths,
            lanes,
            sessions,
            turns,
            search,
            index,
            states,
            usage,
            cron,
            cronScheduler,
            config,
            options,
            hub,
            gate,
            control,
            host);
    }

    public Task<RpcClient> ConnectAsync(
        ClientRole role,
        string? laneId = null,
        string version = RpcProtocol.Version)
        => RpcClient.ConnectAsync(
            Paths.SocketPath,
            new HelloRequest("test-client", version, role, laneId));

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync(CancellationToken.None);
        await Host.DisposeAsync();
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
