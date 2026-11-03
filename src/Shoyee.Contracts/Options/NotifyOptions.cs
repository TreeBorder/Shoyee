using System.Text.Json;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Options;

/// <summary>
/// 纯通知推送配置（与聊天渠道无关、不受窗口期限制）。先选一个供应商，再按供应商 key 取各自的配置节。
///
/// <code>
/// "notify": {
///   "provider": "system",
///   "providers": {
///     "system": { "sound": true },
///     "serverchan": { "configFile": "~/Tools/push/config.sh" },
///     "email": { "host": "smtp.qq.com", "port": 587, "to": ["me@example.com"] }
///   }
/// }
/// </code>
///
/// 未配置整节时按默认值走：<see cref="Provider"/> = <c>system</c>；写 <c>none</c> 关闭通知。
/// </summary>
/// <param name="Provider">选中的供应商 key（默认 <c>system</c>；<c>none</c> = 关闭）。</param>
/// <param name="Providers">供应商 key → 该供应商的配置节（由实现方反序列化）。</param>
public sealed record NotifyOptions(
    string Provider = "system",
    IReadOnlyDictionary<string, JsonElement>? Providers = null)
{
    /// <summary>取指定供应商的配置节并反序列化；没有该节时返回 null。</summary>
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
