using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Options;

namespace Shoyee.Photo.Comfy;

/// <summary>ComfyUI 供应商注册。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 <see cref="IPhotoProvider"/> 的 ComfyUI 实现；配置取 <c>photo.providers.comfy</c>。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="photo">图片配置（可空 = 全部默认）。</param>
    public static IServiceCollection AddShoyeeComfyPhoto(this IServiceCollection services, PhotoOptions? photo = null)
    {
        var options = photo?.GetProviderOptions<ComfyPhotoOptions>("comfy") ?? new ComfyPhotoOptions();

        services.AddHttpClient(ComfyPhotoProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.TryAddSingleton<IPhotoProvider>(sp => new ComfyPhotoProvider(
            options,
            sp.GetRequiredService<WorkspaceConfig>().Root,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(ComfyPhotoProvider.HttpClientName),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<ComfyPhotoProvider>>()));
        return services;
    }
}
