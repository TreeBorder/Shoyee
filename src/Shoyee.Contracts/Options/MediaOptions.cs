using System.Text.Json;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Options;

/// <summary>
/// 图片能力配置：供应商选择 + 共用词表目录；各家字段由实现方反序列化。
///
/// <code>
/// "photo": {
///   "provider": "comfy",
///   "character": "xiaoyi",
///   "promptsDir": ".comfyui/prompts",
///   "providers": {
///     "comfy": { "endpoint": "http://127.0.0.1:8333" }
///   }
/// }
/// </code>
///
/// <see cref="PromptsDir"/> 是**角色图词表**（scene / outfit / action / character /
/// environment / quality / angle 七个 JSON），两种模式共用；工作模式的通用生图不读词表，
/// 直接用模型给的提示词。
/// </summary>
/// <param name="Provider">供应商 key（当前只有 <c>comfy</c>）。</param>
/// <param name="Character">角色图使用的人设 key（<c>prompts/character.json</c> 里的键）；空 = 未配置，角色图工具会提示。</param>
/// <param name="PromptsDir">词表目录（相对工作区）。</param>
/// <param name="Providers">供应商 key → 该供应商的配置节（由实现方反序列化）。</param>
public sealed record PhotoOptions(
    string Provider = "comfy",
    string? Character = "xiaoyi",
    string PromptsDir = ".comfyui/prompts",
    IReadOnlyDictionary<string, JsonElement>? Providers = null)
{
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
