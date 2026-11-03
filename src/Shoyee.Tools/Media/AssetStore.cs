using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Tools.Files;

namespace Shoyee.Tools.Media;

/// <summary>
/// 工作区资产登记（<see cref="IAssetStore"/>）：**不建表**，id 就是相对路径。
///
/// 只认 <c>{workspace}/assets/</c> 下的文件：这是她自己产出（语音/图片/文件）的落点，
/// 不让 <c>asset.get</c> 变成「按相对路径读工作区任意文件」的旁路。
/// 判断按 <see cref="RealPath"/> 解析后的真实路径做，符号链接指到外面也拦得住。
/// </summary>
public sealed class AssetStore : IAssetStore
{
    /// <summary>资产根目录（相对工作区）。</summary>
    public const string Directory = "assets";

    private readonly string _workspace;
    private readonly string _assetsRoot;

    /// <summary>创建登记表。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    public AssetStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        // macOS 的 /var 是指向 /private/var 的链接；工作区根也走真实路径，
        // 否则 RelativePath 会算成 ../../..（两边一个解析过、一个没解析）
        _workspace = RealPath.Resolve(workspaceRoot);
        _assetsRoot = Path.Combine(_workspace, Directory);
    }

    /// <inheritdoc />
    public AssetRef Register(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        var full = RealPath.Resolve(absolutePath);
        var root = RealPath.Resolve(_assetsRoot);
        if (!IsUnder(full, root))
        {
            throw new InvalidOperationException($"资产必须落在工作区 assets/ 下：{absolutePath}");
        }

        var info = new FileInfo(full);
        if (!info.Exists)
        {
            throw new FileNotFoundException("资产文件不存在。", full);
        }

        return new AssetRef(ToId(full), full, AttachmentFactory.GuessMime(full), info.Length);
    }

    /// <inheritdoc />
    public AssetRef? Get(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return null;
        }

        // 绝对路径、空段、. / .. 与反斜杠都按越界处理：id 只接受正斜杠的工作区相对路径
        if (Path.IsPathRooted(assetId) || assetId.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        var segments = assetId.Split('/');
        if (segments.Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            return null;
        }

        var full = RealPath.Resolve(Path.Combine(_workspace, assetId));
        var root = RealPath.Resolve(_assetsRoot);
        if (!IsUnder(full, root) || !File.Exists(full))
        {
            return null;
        }

        return new AssetRef(ToId(full), full, AttachmentFactory.GuessMime(full), new FileInfo(full).Length);
    }

    private string ToId(string full) => Path.GetRelativePath(_workspace, full).Replace('\\', '/');

    private static bool IsUnder(string path, string root)
        => string.Equals(path, root, StringComparison.Ordinal)
           || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
