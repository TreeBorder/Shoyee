using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Gateway.Rpc;

/// <summary>
/// 分发方法用到的全部依赖。集中成一个对象，Gateway 的单元测试可以只给需要的假实现，
/// 不必把整个 DI 容器搬进测试。
/// </summary>
public sealed record RpcServices(
    ISessionService Sessions,
    ITurnService Turns,
    ISearchRepository Search,
    IIndexRepository Index,
    ILaneRepository Lanes,
    IStateRepository States,
    IUsageRepository Usage,
    WorkspaceConfig Config,
    GatewayOptions Options,
    ITurnDrain? Drain = null,
    IReadOnlyList<IChannelAdapter>? Channels = null,
    ICronRepository? Cron = null,
    ICronScheduler? CronScheduler = null,
    INotificationPusher? Notifications = null,
    ITtsService? Tts = null,
    IImageService? Images = null,
    IAssetStore? Assets = null,
    IConfigReloader? ConfigReloader = null,
    IToolCatalog? ToolCatalog = null,
    IMcpControl? Mcp = null,
    ITurnEventSink? Events = null);
