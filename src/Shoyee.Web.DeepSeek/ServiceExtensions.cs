using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Options;
using Shoyee.Web;

namespace Shoyee.Web.DeepSeek;

/// <summary>DeepSeek 网页能力注册（Daemon 组合根按 <see cref="WebOptions.Provider"/> 选择）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 <see cref="IWebProvider"/> 的 DeepSeek 实现；配置取 <c>web.providers.deepseek</c>。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="web">网页配置（可空 = 全部默认）。</param>
    public static IServiceCollection AddShoyeeWebDeepSeek(this IServiceCollection services, WebOptions? web = null)
    {
        var options = web?.GetProviderOptions<DeepSeekWebOptions>("deepseek") ?? new DeepSeekWebOptions();
        services.TryAddSingleton<IWebProvider>(sp =>
            new DeepSeekWebProvider(
                options ?? new DeepSeekWebOptions(),
                sp.GetRequiredService<WorkspaceConfig>().Models));
        return services;
    }
}
