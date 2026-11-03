using System.Reflection;

namespace Shoyee.Photo;

/// <summary>
/// 把内置的 <c>.comfyui</c> 词表/工作流模板复制到工作区：缺哪个补哪个，**已有文件绝不覆盖**
/// （用户改过的词表是权威）。资源名形如 <c>comfyui/prompts/scene.json</c>。
/// </summary>
public static class PhotoWorkspaceTemplates
{
    /// <summary>补齐工作区的 <c>.comfyui</c> 目录。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <returns>本次新建的相对路径。</returns>
    public static IReadOnlyList<string> Ensure(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var assembly = typeof(PhotoWorkspaceTemplates).Assembly;
        var created = new List<string>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith("comfyui/", StringComparison.Ordinal))
            {
                continue;
            }

            var relative = resource["comfyui/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(workspaceRoot, ".comfyui", relative);
            if (File.Exists(path))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                continue;
            }

            using var file = File.Create(path);
            stream.CopyTo(file);
            created.Add(Path.Combine(".comfyui", relative));
        }

        return created;
    }
}
