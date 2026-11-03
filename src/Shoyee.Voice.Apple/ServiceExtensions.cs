using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Options;
using Shoyee.Voice;

namespace Shoyee.Voice.Apple;

/// <summary>Apple 本地转写注册（Daemon 组合根按 <c>voice.transcriptionProvider</c> 选择）。</summary>
public static class ServiceExtensions
{
    /// <summary>注册 <see cref="ISpeechTranscriber"/> 的 Apple 实现；配置取 <c>voice.providers.apple</c>。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="voice">语音配置（可空 = 全部默认）。</param>
    public static IServiceCollection AddShoyeeAppleTranscription(
        this IServiceCollection services,
        VoiceOptions? voice = null)
    {
        var options = voice?.GetProviderOptions<AppleVoiceOptions>("apple") ?? new AppleVoiceOptions();
        services.TryAddSingleton<ISpeechTranscriber>(sp =>
            new AppleSpeechTranscriber(options, sp.GetService<ILogger<AppleSpeechTranscriber>>()));
        return services;
    }
}
