using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Options;
using Shoyee.Sessions.Lifecycle;
using Shoyee.Sessions.Persistence;
using Shoyee.Sessions.Persona;
using Shoyee.Sessions.Roleplay;
using Shoyee.Sessions.Summary;

namespace Shoyee.Sessions;

/// <summary>会话层注册入口（Daemon 组合根调用）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册人设文件、会话工厂、RP 组装器与摘要器。</summary>
    /// <param name="services">服务集合。</param>
    /// <remarks>
    /// <see cref="RoundRecorder"/> 不在容器里当共享单例——它带每轮缓冲，应该随 agent 装配。
    /// </remarks>
    public static IServiceCollection AddShoyeeSessions(this IServiceCollection services)
    {
        services.TryAddSingleton(static sp => new PersonaFiles(sp.GetRequiredService<WorkspaceConfig>().Root));
        services.TryAddSingleton<SessionFactory>();
        // 导出落盘需要工作区根；Sessions 自己不认识 WorkspaceConfig，在这里显式取一次
        services.TryAddSingleton(static sp => new SessionCatalog(
            sp.GetRequiredService<Storage.Abstractions.ISessionRepository>(),
            sp.GetRequiredService<Storage.Abstractions.IMessageRepository>(),
            sp.GetRequiredService<Storage.Abstractions.ILaneRepository>(),
            sp.GetRequiredService<WorkspaceConfig>().Root));
        services.TryAddSingleton<RoleplayContextBuilder>();
        services.TryAddSingleton<RoleplaySummarizer>();
        services.TryAddSingleton<SessionTranscript>();
        return services;
    }
}
