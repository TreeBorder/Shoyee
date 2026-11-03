using System.ComponentModel;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Media;

namespace Shoyee.Tools.Files;

/// <summary>
/// <c>send_file</c>：把一个本地文件挂到本轮回复上（语音/图片之外的通用出口）。
///
/// 只允许工作区与笔记库根内的文件：这两种模式都没有「把任意磁盘文件发出去」的需求，
/// 而 RP 没有审批中间件——放开门槛就等于给她一个静默外发的通道。
/// 产出的附件由 <see cref="OutboundScope"/> 收集，轮末挂到最后一条助手消息上。
/// </summary>
public static class SendFileTool
{
    /// <summary>工具名（MCP 防冒名保留名）。</summary>
    public const string ToolName = "send_file";

    /// <summary>构建工具。</summary>
    /// <param name="config">工作区配置（根目录 + 路径黑名单）。</param>
    /// <param name="notesRoot">笔记库本地根（可空）。</param>
    /// <param name="pathPolicy">共享路径策略（null = 按配置新建）。</param>
    public static ToolDescriptor Create(
        WorkspaceConfig config,
        string? notesRoot = null,
        ToolPathPolicy? pathPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var roots = new List<string> { config.Root };
        if (!string.IsNullOrWhiteSpace(notesRoot))
        {
            roots.Add(notesRoot);
        }

        var allowed = roots
            .Select(RealPath.Resolve)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var policy = pathPolicy ?? ToolPathPolicy.FromConfig(config);

        var tool = AIFunctionFactory.Create(
            ([Description("要发送的本地文件路径（绝对路径，或相对工作区根）。")] string path) =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return "发送失败：路径为空。";
                }

                var full = Path.IsPathRooted(path) ? path : Path.Combine(config.Root, path);
                if (!File.Exists(full))
                {
                    return $"发送失败：文件不存在（{full}）。";
                }

                if (policy.Check(full) is { } denied)
                {
                    return "发送失败：" + denied;
                }

                var real = RealPath.Resolve(full);
                if (!IsInside(real, allowed))
                {
                    return $"发送失败：只能发送工作区或笔记库里的文件（{real} 不在允许范围内）。";
                }

                try
                {
                    var attachment = AttachmentFactory.FromFile(real);
                    OutboundScope.Current?.Add(attachment);
                    return $"已附加文件：{attachment.FileName}（{attachment.Mime}，{attachment.SizeBytes / 1024.0:F1} KB），会随本轮回复一起发给用户。";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    return "发送失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = ToolName,
                Description = "把工作区/笔记库里的一个现有文件发给用户（随这条回复一起送达）。"
                              + "自己生成的语音/图片不用它，voice_speak 与 image_generate 会自动附上。",
            });

        return ToolDescriptor.Create(tool, ToolScope.All, writes: false, requiresApproval: false);
    }

    private static bool IsInside(string path, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (string.Equals(path, root, StringComparison.Ordinal)
                || path.StartsWith(
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
