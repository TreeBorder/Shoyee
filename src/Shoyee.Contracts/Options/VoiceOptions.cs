using System.Text.Json;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Options;

/// <summary>
/// 语音配置：合成与转写**各自**可以选不同供应商（Apple 只做转写，Mimo 两个方向都有）；
/// 供应商的配置节放 <see cref="Providers"/>，各家字段不同，由实现方反序列化。
///
/// <code>
/// "voice": {
///   "provider": "mimo",
///   "synthesisProvider": "mimo",
///   "transcriptionProvider": "apple",
///   "providers": {
///     "mimo": { "baseUrl": "https://api.xiaomimimo.com/v1", "apiKeyEnv": "XIAOMI_API_KEY" },
///     "apple": { "language": "zh-CN" }
///   }
/// }
/// </code>
///
/// <see cref="Provider"/> 是两个方向的默认值，<see cref="SynthesisProvider"/> /
/// <see cref="TranscriptionProvider"/> 省略时跟随它；写 <c>"none"</c> 表示**关闭该方向**
/// （例如只想要本地转写：<c>"provider": "apple", "synthesisProvider": "none"</c>）。
/// </summary>
/// <param name="Provider">默认供应商 key（两向都未单独指定时使用）。</param>
/// <param name="SynthesisProvider">语音合成供应商 key；空 = 跟随 <paramref name="Provider"/>。</param>
/// <param name="TranscriptionProvider">语音转写供应商 key；空 = 跟随 <paramref name="Provider"/>。</param>
/// <param name="Providers">供应商 key → 该供应商的配置节（由实现方反序列化）。</param>
public sealed record VoiceOptions(
    string Provider = "mimo",
    string? SynthesisProvider = null,
    string? TranscriptionProvider = null,
    IReadOnlyDictionary<string, JsonElement>? Providers = null)
{
    /// <summary>实际使用的合成供应商 key；<c>none</c> = 关闭语音合成。</summary>
    public string ResolveSynthesisProvider()
        => string.IsNullOrWhiteSpace(SynthesisProvider) ? Provider.Trim() : SynthesisProvider.Trim();

    /// <summary>实际使用的转写供应商 key；<c>none</c> = 关闭语音转写。</summary>
    public string ResolveTranscriptionProvider()
        => string.IsNullOrWhiteSpace(TranscriptionProvider) ? Provider.Trim() : TranscriptionProvider.Trim();

    /// <summary>取指定供应商的配置节并反序列化；没有该节时返回默认值。</summary>
    /// <typeparam name="T">供应商自己的配置类型。</typeparam>
    /// <param name="provider">供应商 key。</param>
    public T? GetProviderOptions<T>(string provider)
    {
        if (Providers is not null
            && Providers.TryGetValue(provider, out var element)
            && element.ValueKind == JsonValueKind.Object)
        {
            return element.Deserialize<T>(JsonWire.Options);
        }

        return default;
    }
}
