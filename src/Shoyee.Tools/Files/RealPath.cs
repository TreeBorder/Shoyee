namespace Shoyee.Tools.Files;

/// <summary>
/// 路径真实化：沿路径逐段解析符号链接，得到最终落点（不要求末端存在）。
/// 用途：审批白名单按「真实路径」判断，防止工作区内一个指向外部的符号链接绕过审批。
/// </summary>
public static class RealPath
{
    /// <summary>解析 <paramref name="path"/> 的真实路径；解析失败的段原样保留。</summary>
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        var current = root;
        var segments = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            try
            {
                FileSystemInfo info = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);

                if (info.Exists && info.LinkTarget is not null)
                {
                    current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
                }
            }
            catch (IOException)
            {
                // 解析不了就按当前形态继续（最终白名单比较会保守地判为外部）
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }

        return Path.GetFullPath(current);
    }
}
