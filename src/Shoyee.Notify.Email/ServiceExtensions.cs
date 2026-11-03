using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;

namespace Shoyee.Notify.Email;

/// <summary>邮件通知注册（Daemon 组合根按 <see cref="NotifyOptions.Provider"/> 选择）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 <see cref="INotificationPusher"/> 的 SMTP 实现；配置取 <c>notify.providers.email</c>。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="notify">通知配置（可空 = 默认值）。</param>
    public static IServiceCollection AddShoyeeEmailNotify(
        this IServiceCollection services,
        NotifyOptions? notify = null)
    {
        var options = notify?.GetProviderOptions<EmailOptions>(SmtpEmailPusher.ProviderKey)
            ?? new EmailOptions();
        services.TryAddSingleton<INotificationPusher>(_ => new SmtpEmailPusher(options));
        return services;
    }
}
