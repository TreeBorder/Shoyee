using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Shoyee.Contracts.Rpc;

/// <summary>IPC 线上 JSON 的统一序列化选项（camelCase、中文不转义、忽略未知字段）。</summary>
public static class JsonWire
{
    /// <summary>共享只读选项；客户端与服务端必须使用同一份。</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = false,
        };
        return options;
    }
}
