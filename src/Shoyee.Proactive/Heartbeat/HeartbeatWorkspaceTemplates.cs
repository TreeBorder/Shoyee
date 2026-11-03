using System.Reflection;

namespace Shoyee.Proactive.Heartbeat;

/// <summary>
/// 把内置的 <c>.heartbeat/prompts.json</c> 补到工作区：缺才补，**已有文件绝不覆盖**
/// （用户改过的提示词是权威）。资源名形如 <c>heartbeat/prompts.json</c>。
/// </summary>
public static class HeartbeatWorkspaceTemplates
{
    /// <summary>补齐工作区的 <c>.heartbeat</c> 目录。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <returns>本次新建的相对路径。</returns>
    public static IReadOnlyList<string> Ensure(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var assembly = typeof(HeartbeatWorkspaceTemplates).Assembly;
        var created = new List<string>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith("heartbeat/", StringComparison.Ordinal))
            {
                continue;
            }

            var relative = resource["heartbeat/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(workspaceRoot, ".heartbeat", relative);
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
            created.Add(Path.Combine(".heartbeat", relative));
        }

        return created;
    }
}
