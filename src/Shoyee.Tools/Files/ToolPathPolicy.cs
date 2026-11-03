using Shoyee.Contracts.Options;

namespace Shoyee.Tools.Files;

/// <summary>
/// 工具路径黑名单：**默认禁止**读写图片产出目录（<c>{workspace}/assets/images</c>），
/// 另外可用 <see cref="ToolOptions.DeniedPaths"/> 追加目录（相对工作区根，也可写绝对路径）。
///
/// 为什么不只是 UI 过滤：角色图可能是模型不该理解的内容，<c>analyze_image</c> 会把它送去视觉端点，
/// 文件工具会把它当文本读出来。所以闸口放在工具层：图片分析、读文件、列目录、grep、写删改统一用
/// 这里的判断；符号链接按 <see cref="RealPath"/> 解析后再比，防止工作区里的链接指进去。
/// </summary>
public sealed class ToolPathPolicy
{
    private volatile string[] _deniedRoots;

    /// <summary>创建策略。</summary>
    /// <param name="deniedRoots">禁止访问的根目录（绝对路径）。</param>
    public ToolPathPolicy(IEnumerable<string> deniedRoots)
    {
        ArgumentNullException.ThrowIfNull(deniedRoots);
        _deniedRoots = Normalize(deniedRoots);
    }

    /// <summary>
    /// 热更新额外排除目录（<c>config.reload</c> 的 <c>tools.deniedPaths</c>）。
    /// 只换黑名单内容，不动工具定义，所以不影响模型请求前缀。
    /// </summary>
    /// <param name="config">新的工作区配置。</param>
    public void Reload(WorkspaceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _deniedRoots = Normalize(DeniedRootsOf(config));
    }

    private static string[] Normalize(IEnumerable<string> roots)
        => roots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => RealPath.Resolve(root))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>按工作区配置组装：图片产出目录 + 配置里的额外目录。</summary>
    /// <param name="config">工作区配置。</param>
    public static ToolPathPolicy FromConfig(WorkspaceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new ToolPathPolicy(DeniedRootsOf(config));
    }

    private static IEnumerable<string> DeniedRootsOf(WorkspaceConfig config)
    {
        var relative = new List<string> { Shoyee.Photo.PhotoService.OutputDirectory };
        if (config.Tools.DeniedPaths is { Count: > 0 } extra)
        {
            relative.AddRange(extra);
        }

        return relative.Select(path => Path.IsPathRooted(path) ? path : Path.Combine(config.Root, path));
    }

    /// <summary>该路径（或它所在的目录）是否被禁止访问。</summary>
    /// <param name="path">绝对路径。</param>
    public bool IsDenied(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var target = RealPath.Resolve(path);
        foreach (var root in _deniedRoots)
        {
            if (string.Equals(target, root, StringComparison.Ordinal))
            {
                return true;
            }

            if (target.StartsWith(
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>命中黑名单时返回给模型的拒绝话术；未命中返回 null。</summary>
    /// <param name="path">绝对路径。</param>
    public string? Check(string path)
        => IsDenied(path)
            ? $"禁止访问：{RealPath.Resolve(path)}（该目录在工具排除名单里，通常是图片产出等不该被模型读取的目录）。"
            : null;
}
