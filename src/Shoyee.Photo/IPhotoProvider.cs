namespace Shoyee.Photo;

/// <summary>
/// 图片生成供应商契约。**只负责把提示词变成图片字节**：不读工作区状态、不写文件、
/// 不管角色词表——那些在 <see cref="PhotoService"/> 与工具层。
/// </summary>
public interface IPhotoProvider
{
    /// <summary>供应商 key（配置里用的名字）。</summary>
    string Name { get; }

    /// <summary>是否配置完整（false = 工具不挂载）。</summary>
    bool Configured { get; }

    /// <summary>生成图片（阻塞到出图或超时）。</summary>
    /// <param name="request">提示词、尺寸与 seed。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>图片字节（至少一张）。</returns>
    Task<PhotoResult> GenerateAsync(PhotoRequest request, CancellationToken cancellationToken = default);
}
