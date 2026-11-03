using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Photo;

namespace Shoyee.Tools.Media;

/// <summary>
/// <c>image.render</c> 的实现：按提示词同步出图，落 <c>assets/images/YYYY/MM/</c> 并登记资产。
///
/// 与 <c>image_generate</c> 工具共用 <see cref="PhotoService"/>（同一套落盘与比例），
/// 区别只是入口在 RPC、结果给桌面按钮，不挂到任何助手消息上。
/// </summary>
public sealed class ImageRenderService : IImageService
{
    private readonly PhotoService _photo;
    private readonly IAssetStore _assets;

    /// <summary>创建服务。</summary>
    /// <param name="photo">出图服务（拼词/落盘）。</param>
    /// <param name="assets">资产登记。</param>
    public ImageRenderService(PhotoService photo, IAssetStore assets)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(assets);
        _photo = photo;
        _assets = assets;
    }

    /// <inheritdoc />
    public async Task<ImageRenderResult> RenderAsync(
        ImageRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = request.Prompt?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new RpcException(RpcErrors.InvalidParams, "prompt 不能为空");
        }

        var (width, height) = PhotoAspectRatios.Resolve(request.AspectRatio);
        try
        {
            var images = await _photo.GenerateAsync(
                    new PhotoRequest(
                        prompt,
                        request.Negative?.Trim() ?? string.Empty,
                        width,
                        height,
                        Random.Shared.Next(1, int.MaxValue),
                        FileNamePrefix: "image"),
                    cancellationToken)
                .ConfigureAwait(false);

            var asset = _assets.Register(images[0].Path);
            return new ImageRenderResult(asset.AssetId, asset.Path, asset.Mime, asset.SizeBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            // 供应商的超时/拒绝/返回空图都折成 internal：客户端只需要知道「这次没出图、原因是这个」
            throw new RpcException(RpcErrors.Internal, "出图失败：" + ex.Message);
        }
    }
}
