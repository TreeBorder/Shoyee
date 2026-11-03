using Shoyee.Contracts.Options;

namespace Shoyee.Photo;

/// <summary>
/// 出图编排：拼词 → 调供应商 → 落盘。文件写到
/// <c>{workspace}/assets/images/YYYY/MM/</c>，与语音的 <c>assets/audio</c> 同一套规则；
/// 供应商只回字节，不碰文件系统。
/// </summary>
public sealed class PhotoService
{
    /// <summary>图片产出目录（相对工作区）。工具路径黑名单默认排除它。</summary>
    public const string OutputDirectory = "assets/images";

    private readonly WorkspaceConfig _config;
    private readonly PhotoComposer _composer;
    private readonly IPhotoProvider _provider;

    /// <summary>创建服务。</summary>
    /// <param name="config">工作区配置（落盘根）。</param>
    /// <param name="composer">拼词器。</param>
    /// <param name="provider">生成供应商。</param>
    public PhotoService(WorkspaceConfig config, PhotoComposer composer, IPhotoProvider provider)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(provider);
        _config = config;
        _composer = composer;
        _provider = provider;
    }

    /// <summary>按取景角色图：拼词、生成、落盘。</summary>
    /// <param name="plan">取景。</param>
    /// <param name="now">当前时间（环境时段）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<PhotoAsset>> GenerateAsync(
        PhotoPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var request = await _composer.ComposeAsync(plan, now, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await GenerateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>直接按请求生成（工作模式的通用生图用）。</summary>
    /// <param name="request">请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<PhotoAsset>> GenerateAsync(
        PhotoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _provider.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Images.Count == 0)
        {
            throw new InvalidOperationException("图片生成器没有返回任何图片。");
        }

        var directory = Path.Combine(
            _config.Root,
            OutputDirectory,
            DateTime.Now.ToString("yyyy"),
            DateTime.Now.ToString("MM"));        Directory.CreateDirectory(directory);

        var prefix = Sanitize(request.FileNamePrefix) ?? "image";
        if (prefix.Length > 32)
        {
            prefix = prefix[..32];
        }

        var assets = new List<PhotoAsset>(result.Images.Count);
        foreach (var image in result.Images)
        {
            var extension = image.Extension.StartsWith('.') ? image.Extension : "." + image.Extension;
            var fileName = $"{prefix}-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
            var path = Path.Combine(directory, fileName);
            await File.WriteAllBytesAsync(path, image.Data, cancellationToken).ConfigureAwait(false);
            assets.Add(new PhotoAsset(path, image.Mime, image.Data.LongLength));
        }

        return assets;
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value
            .Where(static c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray());
        return cleaned.Length == 0 ? null : cleaned;
    }
}
