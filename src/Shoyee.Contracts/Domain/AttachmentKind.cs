using System.Text.Json.Serialization;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Contracts.Domain;

/// <summary>附件在展示层的类别；决定桌面用气泡、图片缩略图还是文件卡片。</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<AttachmentKind>))]
public enum AttachmentKind
{
    /// <summary>图片（可缩略图/大图查看）。</summary>
    Image,

    /// <summary>语音（气泡 + 播放）。</summary>
    Audio,

    /// <summary>视频（封面 + 播放）。</summary>
    Video,

    /// <summary>其他文件（文件卡片 + 系统预览/打开）。</summary>
    File,
}
