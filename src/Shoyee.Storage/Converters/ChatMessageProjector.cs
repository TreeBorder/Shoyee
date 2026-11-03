using System.Text.Json;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;

namespace Shoyee.Storage.Converters;

/// <summary>一条消息投影到存储列的结果。</summary>
/// <param name="Role">框架角色原值（<see cref="ChatRole.Value"/>）。</param>
/// <param name="Kind">派生种类。</param>
/// <param name="Content">仅可见正文；工具结果为空串。</param>
/// <param name="ToolName">工具名（工具行）。</param>
/// <param name="Attachments">附件（本地文件引用）；没有为 null。</param>
/// <param name="Thinking">思考文本；没有为 null。</param>
/// <param name="ToolCallId">工具调用 id。</param>
/// <param name="ToolArguments">工具参数（JSON 文本）。</param>
/// <param name="ToolResult">工具返回（文本化并截断）。</param>
public readonly record struct MessageProjection(
    string Role,
    MessageKind Kind,
    string Content,
    string? ToolName,
    IReadOnlyList<MessageAttachment>? Attachments = null,
    string? Thinking = null,
    string? ToolCallId = null,
    string? ToolArguments = null,
    string? ToolResult = null);

/// <summary>
/// 框架 <see cref="ChatMessage"/> → 存储列的唯一投影点（规则见 docs/架构与数据模型.md §3.3）。
/// 反向不做：历史读回直接反序列化 <c>message_json</c>。
/// </summary>
public static class ChatMessageProjector
{
    /// <summary>摘要消息标记（框架压缩分组产物）。</summary>
    public const string SummaryMarker = ChatMessageMarkers.Summary;

    /// <summary>她主动发言标记（心跳/新话题）。</summary>
    public const string SpokenMarker = ChatMessageMarkers.Spoken;

    /// <summary>工具没有返回文本时的占位（不能留空：桌面会把空当成「结果没拿到」）。</summary>
    public const string NoOutput = "（无输出）";

    /// <summary>把一条框架消息投影成存储列。</summary>
    public static MessageProjection Project(ChatMessage message)
    {
        var role = message.Role.Value;
        var contents = message.Contents;
        var text = DisplayTextOf(message) ?? message.Text ?? string.Empty;
        var attachments = AttachmentsOf(message);
        var thinking = ThinkingOf(message);
        var isSummary = HasMarker(message, SummaryMarker);

        // 系统注入按用户身份进上下文，但落成独立种类：桌面默认隐藏、可切换显示，检索不索引。
        if (HasMarker(message, ChatMessageMarkers.SystemInput))
        {
            return new MessageProjection(role, MessageKind.SystemInput, text, null, attachments, thinking);
        }

        if (isSummary)
        {
            return new MessageProjection(role, MessageKind.Summary, text, null, Thinking: thinking);
        }

        if (contents.OfType<FunctionResultContent>().FirstOrDefault() is { } result)
        {
            // 工具结果明细不进可见正文（否则会污染检索），但保留一份给桌面展开。
            // 有的工具确实没有返回值（null / 空串）：落一句文字，否则桌面会把「空」当成「没保存」。
            var detail = Clip(ToDisplay(result.Result));
            return new MessageProjection(
                role,
                MessageKind.ToolResult,
                string.Empty,
                null,
                ToolCallId: result.CallId,
                ToolResult: string.IsNullOrWhiteSpace(detail) ? NoOutput : detail);
        }

        if (contents.OfType<FunctionCallContent>().FirstOrDefault() is { } call)
        {
            return new MessageProjection(
                role,
                MessageKind.ToolCall,
                text,
                call.Name,
                attachments,
                Thinking: thinking,
                ToolCallId: call.CallId,
                ToolArguments: Clip(ArgumentsOf(call)));
        }

        if (HasMarker(message, SpokenMarker))
        {
            return new MessageProjection(role, MessageKind.Spoken, text, null, attachments, thinking);
        }

        if (contents.OfType<TextContent>().Any())
        {
            return new MessageProjection(role, MessageKind.Text, text, null, attachments, thinking);
        }

        // 只有 reasoning / usage / 其他非可见内容：不落消息行
        return new MessageProjection(role, MessageKind.Unknown, string.Empty, null);
    }

    /// <summary>该投影是否值得写一行（有可见正文或工具调用/结果）。</summary>
    public static bool ShouldPersist(MessageProjection projection)
        => projection.Kind is not MessageKind.Unknown
           || projection.Content.Length > 0;

    /// <summary>
    /// 该行的正文是否进 FTS 检索。只索引原始对话（含调用前的正文与主动发言）；
    /// 摘要与工具结果不索引，避免与原文重复命中。
    /// </summary>
    public static bool IsSearchable(MessageProjection projection)
        => projection.Content.Length > 0
           && projection.Kind is MessageKind.Text or MessageKind.Spoken or MessageKind.ToolCall;

    /// <summary>工具参数：有 <c>command</c> 给裸原文（菜单/过程行好读），其余压成 JSON。</summary>
    private static string ArgumentsOf(FunctionCallContent call)
    {
        if (call.Arguments is { Count: > 0 } arguments
            && arguments.TryGetValue("command", out var command)
            && command is not null)
        {
            return command as string ?? JsonSerializer.Serialize(command);
        }

        return JsonSerializer.Serialize(call.Arguments);
    }

    private static string? ThinkingOf(ChatMessage message)
    {
        var text = string.Concat(message.Contents.OfType<TextReasoningContent>()
            .Select(static content => content.Text));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ToDisplay(object? result) => result switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(result),
    };

    /// <summary>投影列只给展示：太长就截断（真源仍在 <c>message_json</c> / 会话状态里）。</summary>
    private static string? Clip(string? text, int max = 4000)
        => text is null || text.Length <= max ? text : text[..max] + "…";

    private static string? DisplayTextOf(ChatMessage message)
        => message.AdditionalProperties is { } props
           && props.TryGetValue(ChatMessageMarkers.DisplayText, out var value)
           && value is string text
            ? text
            : null;

    private static IReadOnlyList<MessageAttachment>? AttachmentsOf(ChatMessage message)
        => message.AdditionalProperties is { } props
           && props.TryGetValue(ChatMessageMarkers.Attachments, out var value)
           && value is IReadOnlyList<MessageAttachment> attachments
            ? attachments
            : null;

    private static bool HasMarker(ChatMessage message, string key)
        => message.AdditionalProperties is { } props
           && props.TryGetValue(key, out var value)
           && value is true;
}
