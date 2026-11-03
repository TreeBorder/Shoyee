using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 入站附件落地：目录约定只在实现里有一份（<c>{workspace}/inbox/YYYY/MM/</c>），
/// 适配器只管把字节交进来，不自己拼路径。
/// </summary>
public interface IInboundAttachmentStore
{
    /// <summary>落盘一个附件并返回可交给工具/模型的本地引用。</summary>
    /// <param name="fileName">原始文件名（用于后缀与展示；路径分隔符会被清掉）。</param>
    /// <param name="mime">MIME 类型；未知传 null。</param>
    /// <param name="data">文件字节。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<InboundAttachment> SaveAsync(
        string fileName,
        string? mime,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);
}
