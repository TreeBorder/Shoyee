using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Dtos;

/// <summary>一条消息的展示形态（桌面气泡/过程行/IM 整条）。</summary>
/// <param name="Id">库内自增 id（翻页游标）。</param>
/// <param name="RoundNo">所属轮次；工作模式为 null。</param>
/// <param name="Role">角色。</param>
/// <param name="Kind">种类。</param>
/// <param name="Text">可见文本；工具行为摘要。</param>
/// <param name="ToolName">工具名（kind 为工具时）。</param>
/// <param name="CreatedAt">时间（UTC）。</param>
/// <param name="Attachments">附件。</param>
/// <param name="Thinking">思考（reasoning）文本；只有框架历史里带了 reasoning 才有。</param>
/// <param name="ToolCallId">工具调用 id（把调用行与结果行配成一对）。</param>
/// <param name="ToolArguments">工具参数（JSON 文本；只有调用行有）。</param>
/// <param name="ToolResult">工具返回（文本化后截断；只有结果行有）。</param>
public sealed record MessageView(
    long Id,
    int? RoundNo,
    MessageRole Role,
    MessageKind Kind,
    string Text,
    string? ToolName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MessageAttachment>? Attachments,
    string? Thinking = null,
    string? ToolCallId = null,
    string? ToolArguments = null,
    string? ToolResult = null);

/// <summary>
/// 消息附件（本地文件引用；**路径是唯一真源**，同机客户端直接读盘，不走 HTTP）。
///
/// <paramref name="DurationMs"/> 只有语音/视频才有，给桌面画时长用；拿不到就 null。
/// </summary>
/// <param name="Path">本地绝对路径。</param>
/// <param name="FileName">原始文件名（展示用）。</param>
/// <param name="Mime">MIME 类型。</param>
/// <param name="SizeBytes">字节数。</param>
/// <param name="Kind">展示类别。</param>
/// <param name="DurationMs">时长（毫秒，可空）。</param>
public sealed record MessageAttachment(
    string Path,
    string FileName,
    string Mime,
    long SizeBytes,
    AttachmentKind Kind,
    int? DurationMs = null);

/// <summary>资产（图片/音频/文件）的本地落点。</summary>
/// <param name="AssetId">资产 id。</param>
/// <param name="Path">绝对路径（同机可直读）。</param>
/// <param name="Mime">MIME 类型。</param>
/// <param name="SizeBytes">字节数。</param>
public sealed record AssetRef(string AssetId, string Path, string Mime, long SizeBytes);
