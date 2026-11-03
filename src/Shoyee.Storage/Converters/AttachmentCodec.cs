using System.Text.Json;
using System.Text.Json.Serialization;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Storage.Converters;

/// <summary><c>messages.attachments_json</c> 的编解码（只存本地文件引用，不存字节）。</summary>
public static class AttachmentCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter<AttachmentKind>(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>序列化附件列表；空/null 返回 null（列写 NULL）。</summary>
    public static string? Serialize(IReadOnlyList<MessageAttachment>? attachments)
        => attachments is { Count: > 0 } ? JsonSerializer.Serialize(attachments, Options) : null;

    /// <summary>反序列化；空/坏数据返回 null（投影坏了不影响读消息）。</summary>
    public static IReadOnlyList<MessageAttachment>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<MessageAttachment>>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
