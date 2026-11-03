using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Dtos;

/// <summary>入站附件类别；决定管线把它交给视觉/转写还是当普通文件。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<InboundAttachmentKind>))]
public enum InboundAttachmentKind
{
    /// <summary>图片（可交 <c>analyze_image</c>）。</summary>
    Image,

    /// <summary>语音（可交转写）。</summary>
    Audio,

    /// <summary>其他文件。</summary>
    File,
}

/// <summary>
/// 已落到本地的入站附件。渠道只负责下载，路径约定由 <see cref="Abstractions.IInboundAttachmentStore"/> 统一。
/// </summary>
/// <param name="FileName">原始文件名（可能被清洗）。</param>
/// <param name="Mime">MIME 类型；渠道没给就是空串。</param>
/// <param name="LocalPath">本地绝对路径。</param>
/// <param name="SizeBytes">字节数。</param>
/// <param name="Kind">类别。</param>
public sealed record InboundAttachment(
    string FileName,
    string Mime,
    string LocalPath,
    long SizeBytes,
    InboundAttachmentKind Kind);

/// <summary>
/// 规范化后的入站消息：渠道适配器的唯一输出形状，管线只认这个。
/// </summary>
/// <param name="LaneId">所属 Lane（如 <c>mattermost:work</c>）。</param>
/// <param name="ExternalId">渠道侧消息 id，用于去重。</param>
/// <param name="Target">回投地址（渠道自定义的不透明串，原样传给 <see cref="Abstractions.IChannelSender"/>）。</param>
/// <param name="SenderId">发送者 id。</param>
/// <param name="SenderName">发送者展示名。</param>
/// <param name="Text">正文（已剥掉 @bot 前缀）。</param>
/// <param name="AddressedToBot">是否明确指向本人（私聊或 @）；适配器已按此过滤，这里只做记录。</param>
/// <param name="ReceivedAt">收到的时刻（UTC）。</param>
/// <param name="Attachments">已落地的附件。</param>
public sealed record InboundMessage(
    string LaneId,
    string ExternalId,
    string Target,
    string SenderId,
    string? SenderName,
    string Text,
    bool AddressedToBot,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<InboundAttachment>? Attachments = null);
