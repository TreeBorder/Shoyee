namespace Shoyee.Contracts.Dtos;

/// <summary>
/// <c>image.render</c> 的请求：桌面「生成一张图」按钮触发，工作模式直连通用生图。
///
/// 与 agent 的 <c>image_generate</c> 工具同一语义（模型给提示词，不读角色词表）；
/// 角色本人的照片仍走 RP 的 <c>camera</c> / <c>gallery</c>，不从这里出。
/// </summary>
/// <param name="Prompt">画面描述（必填）。</param>
/// <param name="Negative">负向提示词（可空）。</param>
/// <param name="AspectRatio">比例：<c>2:3</c>（默认竖屏）/ <c>3:2</c> / <c>1:1</c>；不认识的值回落 2:3。</param>
public sealed record ImageRenderRequest(
    string Prompt,
    string? Negative = null,
    string? AspectRatio = null);

/// <summary>
/// <c>image.render</c> 的返回：已落盘的图片资产。
///
/// <paramref name="AssetId"/> 同时是 <c>asset.get</c> 的入参；
/// <paramref name="Path"/> 是绝对路径，同机客户端可直接读盘显示。
/// </summary>
/// <param name="AssetId">资产 id（见 <c>asset.get</c>）。</param>
/// <param name="Path">图片文件绝对路径。</param>
/// <param name="Mime">MIME 类型。</param>
/// <param name="SizeBytes">字节数。</param>
public sealed record ImageRenderResult(
    string AssetId,
    string Path,
    string Mime,
    long SizeBytes);

/// <summary>一张已落盘的图片资产（DB 不存二进制，只给绝对路径）。</summary>
/// <param name="Path">本地绝对路径。</param>
/// <param name="Mime">媒体类型。</param>
/// <param name="SizeBytes">字节数。</param>
public sealed record PhotoAssetView(string Path, string Mime, long SizeBytes);

/// <summary>
/// 角色图生成完成（异步出图，与 Harem 的「拍完再发」同语义）。
/// 这不是历史消息——它只是把图推给当前打开的客户端；会话正文里只有她当轮说的话。
/// </summary>
/// <param name="TaskId">生成任务 id（同一次 camera/gallery 调用）。</param>
/// <param name="SessionId">工作会话 id；RP 为 null。</param>
/// <param name="Assets">生成的图片（通常一张）。</param>
/// <param name="Caption">说明（如「角色图」）。</param>
/// <param name="LaneId">所属 Lane（可见性过滤用）。</param>
public sealed record PhotoReadyEvent(
    string TaskId,
    string? SessionId,
    IReadOnlyList<PhotoAssetView> Assets,
    string? Caption = null,
    string? LaneId = null);
