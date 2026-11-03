using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Options;
using Shoyee.Voice;

namespace Shoyee.Voice.Mimo;

/// <summary>MiMo 语音注册（Daemon 组合根按配置选择方向与供应商）。</summary>
/// <remarks>
/// 合成与转写分开注册：同一家供应商可能只担任其中一个方向（例如 TTS 用 Mimo、STT 用 Apple）。
/// 两个方向都选 Mimo 时共用同一个 <see cref="MimoSpeechProvider"/> 实例（同一个 HttpClient）。
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>注册 MiMo 语音合成（<c>voice.providers.mimo</c> 节）。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="voice">语音配置（可空 = 全部默认）。</param>
    public static IServiceCollection AddShoyeeMimoSynthesis(
        this IServiceCollection services,
        VoiceOptions? voice = null)
    {
        services.TryAddSingleton(CreateProvider(voice));
        services.TryAddSingleton<ISpeechSynthesizer>(sp => sp.GetRequiredService<MimoSpeechProvider>());
        return services;
    }

    /// <summary>注册 MiMo 语音转写（<c>voice.providers.mimo</c> 节）。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="voice">语音配置（可空 = 全部默认）。</param>
    public static IServiceCollection AddShoyeeMimoTranscription(
        this IServiceCollection services,
        VoiceOptions? voice = null)
    {
        services.TryAddSingleton(CreateProvider(voice));
        services.TryAddSingleton<ISpeechTranscriber>(sp => sp.GetRequiredService<MimoSpeechProvider>());
        return services;
    }

    private static Func<IServiceProvider, MimoSpeechProvider> CreateProvider(VoiceOptions? voice)
    {
        var options = voice?.GetProviderOptions<MimoVoiceOptions>("mimo") ?? new MimoVoiceOptions();
        return sp => new MimoSpeechProvider(
            options,
            http: null,
            sp.GetService<ILogger<MimoSpeechProvider>>());
    }
}
