using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Options;

namespace Shoyee.Notes.Obsidian;

/// <summary>Obsidian 笔记库注册（Daemon 组合根按 <see cref="NotesOptions.Provider"/> 选择）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 <see cref="INotesProvider"/> 的 Obsidian 实现；配置取 <c>notes.providers.obsidian</c>。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="notes">笔记库配置（可空 = 未配置，检索工具不挂载）。</param>
    public static IServiceCollection AddShoyeeObsidianNotes(this IServiceCollection services, NotesOptions? notes = null)
    {
        var options = notes?.GetProviderOptions<ObsidianNotesOptions>("obsidian");
        services.TryAddSingleton<INotesProvider>(sp =>
            new ObsidianNotesProvider(options, sp.GetRequiredService<WorkspaceConfig>()));
        return services;
    }
}
