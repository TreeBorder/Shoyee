using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Files;
using Shoyee.Tools.Mcp;
using Shoyee.Tools.Process;
using Shoyee.Voice;
using Shoyee.Web;

namespace Shoyee.Tools;

/// <summary>工具面注册入口（Daemon 组合根调用）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册工具组装器与后台进程表。</summary>
    /// <remarks>
    /// MCP 需要异步连接，由 Daemon 在启动时调用 <see cref="McpConnector.ConnectAsync"/> 后注册
    /// <see cref="McpToolset"/> 单例；没注册时组装器自动跳过 MCP。
    /// <see cref="IProcessCompletionSink"/> 目前没有实现（Channels 接入后补），进程结果用 <c>process_status</c> 取。
    /// </remarks>
    public static IServiceCollection AddShoyeeTools(this IServiceCollection services)
    {
        services.TryAddSingleton(static sp =>
        {
            var config = sp.GetRequiredService<WorkspaceConfig>();
            return new ProcessRegistry(
                Path.Combine(config.Root, ".processes"),
                FileAccessTools.DefaultDirectory(config),
                config.Shell?.DenyPatterns,
                sp.GetService<IProcessCompletionSink>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<ProcessRegistry>>());
        });

        services.TryAddSingleton<Shoyee.Contracts.Abstractions.IAssetStore>(static sp =>
            new Shoyee.Tools.Media.AssetStore(sp.GetRequiredService<WorkspaceConfig>().Root));

        // 只有配了合成供应商才有 tts.synthesize；Gateway 拿到 null 时返回明确的「未配置」
        services.TryAddSingleton<Shoyee.Contracts.Abstractions.ITtsService>(static sp =>
        {
            var synthesizer = sp.GetService<ISpeechSynthesizer>();
            return synthesizer is { Configured: true } configured
                ? new Shoyee.Tools.Voice.TtsService(
                    configured,
                    sp.GetRequiredService<Shoyee.Contracts.Abstractions.IAssetStore>(),
                    sp.GetRequiredService<WorkspaceConfig>().Root,
                    sp.GetService<Shoyee.Storage.Abstractions.IMessageRepository>())
                : null!;
        });

        // 只有配了图片供应商才有 image.render；Gateway 拿到 null 时返回明确的「未配置」
        services.TryAddSingleton<Shoyee.Contracts.Abstractions.IImageService>(static sp =>
        {
            var photo = sp.GetService<global::Shoyee.Photo.PhotoService>();
            return photo is null
                ? null!
                : new Shoyee.Tools.Media.ImageRenderService(
                    photo,
                    sp.GetRequiredService<Shoyee.Contracts.Abstractions.IAssetStore>());
        });

        services.TryAddSingleton(static sp => new ToolsetBuilder(
            sp.GetRequiredService<WorkspaceConfig>(),
            new ToolsetServices(
                SessionSearch: sp.GetService<global::Shoyee.Memory.History.ISessionSearch>(),
                Index: sp.GetService<global::Shoyee.Memory.Index.IIndexStore>(),
                Notes: sp.GetService<global::Shoyee.Notes.INotesProvider>(),
                Processes: sp.GetService<ProcessRegistry>(),
                Mcp: sp.GetService<McpToolset>(),
                Web: sp.GetService<IWebProvider>(),
                States: sp.GetService<Shoyee.Storage.Abstractions.IStateRepository>(),
                Synthesizer: sp.GetService<ISpeechSynthesizer>(),
                Transcriber: sp.GetService<ISpeechTranscriber>(),
                Photo: sp.GetService<global::Shoyee.Photo.PhotoService>(),
                PhotoVocabulary: sp.GetService<global::Shoyee.Photo.PhotoPrompts>(),
                PhotoTasks: sp.GetService<global::Shoyee.Photo.PhotoTaskRunner>(),
                Cron: sp.GetService<Shoyee.Storage.Abstractions.ICronRepository>(),
                CronScheduler: sp.GetService<global::Shoyee.Contracts.Abstractions.ICronScheduler>(),
                Messages: sp.GetService<Shoyee.Storage.Abstractions.IMessageRepository>(),
                Events: sp.GetService<Shoyee.Contracts.Abstractions.ITurnEventSink>())));

        return services;
    }
}
