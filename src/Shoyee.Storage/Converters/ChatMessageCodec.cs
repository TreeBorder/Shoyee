using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Shoyee.Storage.Converters;

/// <summary>
/// <see cref="ChatMessage"/> 的 JSON 编解码，**只服务 RP 的轮次存储**：
/// 每轮把 Request/Response 的每条消息原样落库，下一轮再还原成消息列表。
/// 工作模式的历史不走这里（那是框架 session 序列化自己的事）；这里也不做 session 序列化。
/// 必须用框架的默认选项，否则 <c>FunctionCallContent</c> 等多态内容会丢（Secretary 实测）。
/// </summary>
public static class ChatMessageCodec
{
    private static readonly JsonSerializerOptions Options = AgentAbstractionsJsonUtilities.DefaultOptions;

    /// <summary>序列化为可落库的 JSON。</summary>
    public static string Serialize(ChatMessage message)
        => JsonSerializer.Serialize(message, Options.GetTypeInfo(typeof(ChatMessage))!);

    /// <summary>从 JSON 还原完整消息。</summary>
    public static ChatMessage Deserialize(string json)
        => (ChatMessage)JsonSerializer.Deserialize(json, Options.GetTypeInfo(typeof(ChatMessage))!)!;

    /// <summary>序列化一组消息（RP 每轮存 Request/Response 数组）。
    /// 先 <c>ToList()</c>：把 <see cref="IReadOnlyList{T}"/> 交给 <c>List&lt;ChatMessage&gt;</c> 的 TypeInfo 会 InvalidCast。</summary>
    public static string SerializeList(IReadOnlyList<ChatMessage> messages)
        => JsonSerializer.Serialize(messages.ToList(), Options.GetTypeInfo(typeof(List<ChatMessage>))!);

    /// <summary>还原一组消息。</summary>
    public static IReadOnlyList<ChatMessage> DeserializeList(string json)
        => (List<ChatMessage>)JsonSerializer.Deserialize(json, Options.GetTypeInfo(typeof(List<ChatMessage>))!)!;
}
