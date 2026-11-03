using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;
using Shoyee.Gateway.Rpc;
using Shoyee.Gateway.Subscriptions;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Gateway;

/// <summary>传输层注册入口（Daemon 组合根调用）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 UDS 监听、方法分发与事件总线。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="options">进程级参数（socket 路径、版本、工作区）。</param>
    /// <remarks>
    /// 这里会 <see cref="ServiceCollectionDescriptorExtensions.Replace"/> 掉 Agent 注册的
    /// <see cref="NullTurnEventSink"/>，所以必须在 <c>AddShoyeeAgent()</c> 之后调用。
    /// </remarks>
    public static IServiceCollection AddShoyeeGateway(this IServiceCollection services, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.TryAddSingleton<ShutdownGate>();
        services.TryAddSingleton<DaemonControl>();
        services.TryAddSingleton<EventHub>();
        services.Replace(ServiceDescriptor.Singleton<ITurnEventSink>(
            static sp => sp.GetRequiredService<EventHub>()));
        // 在线判定与事件广播共用同一张连接表，避免出现「事件发到了、心跳却以为桌面不在」
        services.TryAddSingleton<IClientPresence>(static sp => sp.GetRequiredService<EventHub>());

        services.TryAddSingleton(static sp => new RpcServices(
            sp.GetRequiredService<ISessionService>(),
            sp.GetRequiredService<ITurnService>(),
            sp.GetRequiredService<ISearchRepository>(),
            sp.GetRequiredService<IIndexRepository>(),
            sp.GetRequiredService<ILaneRepository>(),
            sp.GetRequiredService<IStateRepository>(),
            sp.GetRequiredService<IUsageRepository>(),
            sp.GetRequiredService<WorkspaceConfig>(),
            sp.GetRequiredService<GatewayOptions>(),
            sp.GetService<ITurnDrain>(),
            sp.GetServices<IChannelAdapter>().ToList(),
            sp.GetService<ICronRepository>(),
            sp.GetService<ICronScheduler>(),
            sp.GetService<INotificationPusher>(),
            sp.GetService<ITtsService>(),
            sp.GetService<IImageService>(),
            sp.GetService<IAssetStore>(),
            sp.GetService<IConfigReloader>(),
            sp.GetService<IToolCatalog>(),
            sp.GetService<IMcpControl>(),
            sp.GetService<ITurnEventSink>()));

        services.TryAddSingleton<RpcDispatcher>();
        services.TryAddSingleton<GatewayHost>();
        services.AddHostedService(static sp => sp.GetRequiredService<GatewayHost>());
        return services;
    }
}
