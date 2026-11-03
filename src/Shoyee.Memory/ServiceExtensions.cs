using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Memory.History;
using Shoyee.Memory.Index;

namespace Shoyee.Memory;

/// <summary>检索层注册入口（Daemon 组合根调用）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册会话检索与 INDEX 地址簿（笔记库见 <c>Shoyee.Notes.*</c>）。</summary>
    public static IServiceCollection AddShoyeeMemory(this IServiceCollection services)
    {
        services.TryAddSingleton<ISessionSearch, History.SessionSearch>();
        services.TryAddSingleton<IIndexStore, IndexStore>();
        return services;
    }
}
