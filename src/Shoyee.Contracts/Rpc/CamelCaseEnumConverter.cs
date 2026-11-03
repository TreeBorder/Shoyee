using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shoyee.Contracts.Rpc;

/// <summary>
/// 枚举统一按 camelCase 字符串序列化（<c>"roleplay"</c>），不受调用方 <see cref="JsonSerializerOptions"/> 影响。
/// </summary>
public sealed class CamelCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    /// <summary>创建 camelCase 枚举转换器。</summary>
    public CamelCaseEnumConverter()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}
