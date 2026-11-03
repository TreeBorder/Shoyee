using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Options;

namespace Shoyee.Photo;

/// <summary>图片能力注册（Daemon 组合根按 <see cref="PhotoOptions.Provider"/> 选供应商）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册词表、拼词器、出图服务与异步队列；供应商由 <c>AddShoyee*Photo</c> 另行注册。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="photo">图片配置（可空 = 全部默认）。</param>
    public static IServiceCollection AddShoyeePhoto(this IServiceCollection services, PhotoOptions? photo = null)
    {
        var options = photo ?? new PhotoOptions();

        services.TryAddSingleton(sp =>
        {
            var config = sp.GetRequiredService<WorkspaceConfig>();
            var configured = options.PromptsDir;
            var directory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(config.Root, ".comfyui", "prompts")
                : Path.IsPathRooted(configured) ? configured : Path.Combine(config.Root, configured);
            return new PhotoPrompts(directory);
        });

        services.TryAddSingleton(sp => new PhotoComposer(
            sp.GetRequiredService<PhotoPrompts>(),
            options.Character));

        services.TryAddSingleton<PhotoService>();
        services.TryAddSingleton<PhotoTaskRunner>();
        services.AddHostedService(static sp => sp.GetRequiredService<PhotoTaskRunner>());
        return services;
    }
}
