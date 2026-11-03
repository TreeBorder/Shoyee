using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;

namespace Shoyee.Notify.System;

/// <summary>系统通知注册（Daemon 组合根按 <see cref="NotifyOptions.Provider"/> 选择）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 <see cref="INotificationPusher"/> 的 macOS 系统通知实现；配置取 <c>notify.providers.system</c>。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="notify">通知配置（可空 = 默认值）。</param>
    public static IServiceCollection AddShoyeeSystemNotify(
        this IServiceCollection services,
        NotifyOptions? notify = null)
    {
        var options = notify?.GetProviderOptions<SystemNotifyOptions>(SystemNotificationPusher.ProviderKey)
            ?? new SystemNotifyOptions();
        services.TryAddSingleton<INotificationPusher>(_ => new SystemNotificationPusher(options));
        return services;
    }
}
