using System.Text.Json;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Options;

/// <summary>
/// 笔记库配置：先选供应商，再按供应商 key 取各自的配置节（各家字段可以不同）。
///
/// <code>
/// "notes": {
///   "provider": "obsidian",
///   "providers": {
///     "obsidian": { "vaultRoot": "/path/to/vault", "notesDir": "notes/shoyee" }
///   }
/// }
/// </code>
/// </summary>
/// <param name="Provider">选中的供应商 key（默认 <c>obsidian</c>）。</param>
/// <param name="Providers">供应商 key → 该供应商的配置节（由实现方反序列化）。</param>
public sealed record NotesOptions(
    string Provider = "obsidian",
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
