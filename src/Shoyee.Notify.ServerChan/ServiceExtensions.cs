using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;

namespace Shoyee.Notify.ServerChan;

/// <summary>Server酱³ 注册（Daemon 组合根按 <see cref="NotifyOptions.Provider"/> 选择）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 <see cref="INotificationPusher"/> 的 Server酱实现；配置取 <c>notify.providers.serverchan</c>。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="notify">通知配置（可空 = 默认值）。</param>
    public static IServiceCollection AddShoyeeServerChanNotify(
        this IServiceCollection services,
        NotifyOptions? notify = null)
    {
        var options = notify?.GetProviderOptions<ServerChanOptions>(ServerChanPusher.ProviderKey)
            ?? new ServerChanOptions();
        services.TryAddSingleton<INotificationPusher>(_ => new ServerChanPusher(options));
        return services;
    }
}
