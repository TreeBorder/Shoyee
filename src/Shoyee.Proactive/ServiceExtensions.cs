using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Options;
using Shoyee.Proactive.Cron;
using Shoyee.Proactive.Heartbeat;

namespace Shoyee.Proactive;

/// <summary>主动行为模块注册入口（Daemon 组合根调用）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册心跳与 cron：目标选择、判定服务、Quartz 调度与 cron 执行。</summary>
    /// <param name="services">服务集合。</param>
    /// <remarks>
    /// 依赖 <c>ILaneRepository</c> / <c>IStateRepository</c> / <c>ICronRepository</c>（存储层）、
    /// <c>IClientPresence</c>（Gateway 的 EventHub）、<c>ITurnService</c> / <c>ISessionService</c>（Agent）
    /// 与 <c>IOutboundDelivery</c>（Channels 的投递台账），都由组合根另行注册。
    /// 心跳/cron 未启用时各自退出或空转，不在这里按配置决定装不装。
    /// </remarks>
    public static IServiceCollection AddShoyeeProactive(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<HeartbeatTargetSelector>();
        services.TryAddSingleton(static sp => new HeartbeatPromptBook(
            sp.GetRequiredService<WorkspaceConfig>().Root));
        services.TryAddSingleton<HeartbeatService>();
        services.AddHostedService<HeartbeatScheduler>();

        // Quartz：内存调度（cron_jobs 才是定义真源，启动时重排；去重靠执行台账唯一约束）
        services.AddQuartz(q => q.UseDefaultThreadPool(maxConcurrency: 5));
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.TryAddSingleton<ICronScheduler, CronJobScheduler>();
        services.TryAddSingleton<ICronScriptRunner, CronScriptRunner>();
        services.TryAddSingleton<CronRunner>();
        services.AddHostedService<CronBootstrapService>();

        return services;
    }
}
