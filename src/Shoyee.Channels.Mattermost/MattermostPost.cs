namespace Shoyee.Channels.Mattermost;

/// <summary>Mattermost 帖子（<c>posted</c> WS 事件里的 post 对象，双重 JSON 编码）。</summary>
/// <param name="Id">帖子 id。</param>
/// <param name="UserId">作者 id。</param>
/// <param name="ChannelId">频道 id。</param>
/// <param name="Message">正文。</param>
/// <param name="RootId">thread 根帖 id；空 = 本帖即根帖。</param>
/// <param name="Type">系统消息类型；空 = 用户消息。</param>
/// <param name="ChannelType">频道类型 D/G/P/O（事件 data 层字段）。</param>
/// <param name="Mentioned">是否 @ 了 bot。</param>
/// <param name="FileIds">附件文件 id（<c>file_ids</c> / <c>metadata.files</c>）。</param>
public sealed record MattermostPost(
    string Id,
    string UserId,
    string ChannelId,
    string Message,
    string RootId,
    string Type,
    string ChannelType,
    bool Mentioned,
    IReadOnlyList<string> FileIds);
