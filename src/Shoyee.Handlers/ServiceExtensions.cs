using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Handlers.Commands;

namespace Shoyee.Handlers;

/// <summary>预置指令注册入口（Daemon 组合根调用）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册指令处理器与路由。</summary>
    /// <param name="services">服务集合。</param>
    /// <remarks>
    /// 依赖 <see cref="Shoyee.Contracts.Abstractions.IApprovalConsole"/>（Agent 的审批台）与
    /// <see cref="Shoyee.Contracts.Abstractions.ITurnService"/>（Agent 的轮次服务），
    /// 所以必须在 <c>AddShoyeeAgent()</c> 之后注册。
    /// </remarks>
    public static IServiceCollection AddShoyeeHandlers(this IServiceCollection services)
    {
        // 会话控制面：模型没有、也不该有「切会话/改名」的工具
        services.AddSingleton<ICommandHandler, NewCommand>();
        services.AddSingleton<ICommandHandler, ListCommand>();
        services.AddSingleton<ICommandHandler, ResumeCommand>();
        services.AddSingleton<ICommandHandler, TitleCommand>();

        // 事实查询：确定性读取，不问模型（问出来会是角色口吻）
        services.AddSingleton<ICommandHandler, StateCommand>();
        services.AddSingleton<ICommandHandler, CronCommand>();

        // 必须绕过 LLM 的确定性入口
        services.AddSingleton<ICommandHandler, ApproveCommand>();
        services.AddSingleton<ICommandHandler, RejectCommand>();
        services.AddSingleton<ICommandHandler, StopCommand>();
        services.AddSingleton<ICommandHandler, HelpCommand>();
        services.TryAddSingleton<CommandRouter>();
        return services;
    }
}
