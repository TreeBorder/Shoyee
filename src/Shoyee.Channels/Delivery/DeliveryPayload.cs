using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shoyee.Channels.Delivery;

/// <summary>台账负载的种类；落库后靠它知道重试时该调哪个发送方法。</summary>
internal enum DeliveryPayloadKind
{
    Text,
    File,
}

/// <summary>
/// 投递台账的负载（`deliveries.payload_json`）。
///
/// 刻意只放「重发所需的最小信息」：目标地址 + 正文/文件路径。
/// 大块内容不进库——图片/语音落地后传路径，正文按原样存。
/// </summary>
internal sealed record DeliveryPayload
{
    [JsonConverter(typeof(JsonStringEnumConverter<DeliveryPayloadKind>))]
    public DeliveryPayloadKind Kind { get; init; }

    public string Target { get; init; } = string.Empty;

    public string? Text { get; init; }

    public string? FilePath { get; init; }

    public string? Caption { get; init; }

    public static DeliveryPayload ForText(string target, string text) => new()
    {
        Kind = DeliveryPayloadKind.Text,
        Target = target,
        Text = text,
    };

    public static DeliveryPayload ForFile(string target, string filePath, string? caption) => new()
    {
        Kind = DeliveryPayloadKind.File,
        Target = target,
        FilePath = filePath,
        Caption = caption,
    };

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static DeliveryPayload FromJson(string json) => JsonSerializer.Deserialize<DeliveryPayload>(json, Json)
        ?? throw new FormatException("投递负载为空");
}
