using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 桌面「生成一张图」按钮的出口：按提示词同步出一张图并落盘，返回资产。
///
/// 与 agent 的 <c>image_generate</c> 工具走同一个 <c>PhotoService</c>；区别只在于谁触发。
/// 未配置图片供应商时不注册本服务，RPC 返回明确的「未配置」。
/// </summary>
public interface IImageService
{
    /// <summary>生成并落盘（等到出图完成；通常几十秒到几分钟）。</summary>
    /// <param name="request">提示词与比例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ImageRenderResult> RenderAsync(
        ImageRenderRequest request,
        CancellationToken cancellationToken = default);
}
