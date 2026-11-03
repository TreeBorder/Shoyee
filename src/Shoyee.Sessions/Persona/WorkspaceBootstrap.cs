using Shoyee.Contracts.Domain;

namespace Shoyee.Sessions.Persona;

/// <summary>
/// 首次启动写工作区模板：缺哪个补哪个，**已有文件绝不覆盖**（人工编辑为权威）。
/// 只负责人设/守则文件；<c>config.json</c> 的引导留给 Daemon 启动流程。
/// </summary>
public static class WorkspaceBootstrap
{
    /// <summary>补齐工作区人设文件与模式目录。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    public static WorkspaceBootstrapResult EnsurePersonaFiles(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        Directory.CreateDirectory(workspaceRoot);

        var templates = new (string RelativePath, string Content)[]
        {
            (PersonaFiles.SharedAgentsFileName, WorkspaceTemplates.SharedAgents),
            (PersonaFiles.SoulFileName, WorkspaceTemplates.Soul),
            (PersonaFiles.UserFileName, WorkspaceTemplates.User),
            (PersonaFiles.MemoryFileName, WorkspaceTemplates.Memory),
            (PersonaFiles.ModeAgentsRelativePath(Mode.Work), WorkspaceTemplates.WorkAgents),
            (PersonaFiles.ModeAgentsRelativePath(Mode.Roleplay), WorkspaceTemplates.RoleplayAgents),
            (PersonaFiles.WorldRelativePath, WorkspaceTemplates.World),
        };

        var created = new List<string>();
        foreach (var (relativePath, content) in templates)
        {
            var path = Path.Combine(workspaceRoot, relativePath);
            if (File.Exists(path))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);
            created.Add(relativePath);
        }

        var messages = new List<string>();
        if (created.Count > 0)
        {
            messages.Add($"已生成工作区模板：{string.Join("、", created)}"
                + "（起点而已，直接编辑；工作模式对新会话生效，RP 下一轮生效）");
        }

        return new WorkspaceBootstrapResult(created, messages);
    }
}
