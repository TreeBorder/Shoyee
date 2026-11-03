using System.Text.Json.Serialization;

namespace Shoyee.Voice.Mimo;

/// <summary>MiMo <c>/chat/completions</c> 的请求体（合成与转写共用；字段为空时不发）。</summary>
internal sealed class MimoChatRequest
{
    /// <summary>模型 id。</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>消息列表。</summary>
    [JsonPropertyName("messages")]
    public required IReadOnlyList<MimoMessage> Messages { get; init; }

    /// <summary>合成的音频参数（转写时不发）。</summary>
    [JsonPropertyName("audio")]
    public MimoAudioOptions? Audio { get; init; }

    /// <summary>转写参数（合成时不发）。注意是**顶层**字段，不是嵌套在 extra_body 里。</summary>
    [JsonPropertyName("asr_options")]
    public MimoAsrOptions? AsrOptions { get; init; }
}

/// <summary>一条消息。<c>content</c> 可以是字符串（合成/设计）或内容块数组（转写）。</summary>
/// <param name="Role">角色。</param>
/// <param name="Content">内容。</param>
internal sealed record MimoMessage(string Role, object Content);

/// <summary>合成的输出参数。</summary>
/// <param name="Format">输出格式（wav / mp3）。</param>
/// <param name="Voice">音色 id；音色设计模式不发。</param>
internal sealed record MimoAudioOptions(
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("voice"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Voice = null);

/// <summary>转写参数。</summary>
/// <param name="Language">语种：auto / zh / en。</param>
internal sealed record MimoAsrOptions([property: JsonPropertyName("language")] string Language);

/// <summary>转写请求里的音频内容块。</summary>
/// <param name="Data">data URL（<c>data:audio/wav;base64,…</c>）。</param>
internal sealed record MimoAudioInput([property: JsonPropertyName("data")] string Data);

/// <summary>内容块：目前只有 input_audio 一种。</summary>
/// <param name="Type">块类型，固定 <c>input_audio</c>。</param>
/// <param name="InputAudio">音频数据。</param>
internal sealed record MimoContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("input_audio")] MimoAudioInput InputAudio);

/// <summary>响应体。</summary>
internal sealed class MimoChatResponse
{
    /// <summary>候选列表。</summary>
    [JsonPropertyName("choices")]
    public IReadOnlyList<MimoChoice>? Choices { get; init; }
}

/// <summary>一个候选。</summary>
internal sealed class MimoChoice
{
    /// <summary>消息。</summary>
    [JsonPropertyName("message")]
    public MimoResponseMessage? Message { get; init; }
}

/// <summary>响应消息：合成的音频在 <see cref="Audio"/>，转写的文本在 <see cref="Content"/>。</summary>
internal sealed class MimoResponseMessage
{
    /// <summary>文本（转写结果）。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>音频（合成结果）。</summary>
    [JsonPropertyName("audio")]
    public MimoAudioPayload? Audio { get; init; }
}

/// <summary>合成返回的音频。</summary>
internal sealed class MimoAudioPayload
{
    /// <summary>base64 音频字节。</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>供应商回带的文本（参考用）。</summary>
    [JsonPropertyName("transcript")]
    public string? Transcript { get; init; }
}
