using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;

namespace Shoyee.Sessions.Persona;

/// <summary>
/// 前置提示词拼装（不读盘，只负责把 <see cref="PersonaContent"/> 排成稳定顺序）。
///
/// **排序原则：越不容易变的人设放越前面，越容易追加的放越后面**——
/// 文件改动只让「它之后的段」前缀失效，不会把整套提示词打乱。
/// 固定为：AGENTS.shared → modes/{mode}/AGENTS → SOUL → WORLD → USER → INDEX → MEMORY。
/// 纪律先于人设：先用 AGENTS 约束行为，再交代她是谁。
///
/// - 工作模式：拼成<b>一份</b> system 文本，会话创建时写进 session 第一条消息（之后随会话序列化，不再拼）。
/// - RP：拼成<b>多条独立 system 消息</b>（背景区），每轮由 RoleplayContextBuilder 重新调用。
/// </summary>
public static class PromptAssembler
{
    /// <summary>
    /// 工作模式的单份 system 文本：AGENTS.shared → modes/work/AGENTS → SOUL → USER → INDEX 概览 → MEMORY。
    /// </summary>
    /// <param name="content">人设文本。</param>
    /// <param name="indexDirectory">INDEX 目录渲染文本（可选；空 = 不注入该节）。</param>
    public static string BuildWorkSystemPrompt(PersonaContent content, string? indexDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var sections = new (string Header, string? Body)[]
        {
            ("# 行为守则（AGENTS.shared.md）\n", content.SharedAgents),
            ("# 工作守则（modes/work/AGENTS.md）\n", content.ModeAgents),
            ("# 人设（SOUL.md）\n", content.Soul),
            ("# 用户画像（USER.md）\n", content.User),
            ("# 资料索引（INDEX）\n", indexDirectory),
            ("# 长期记忆（MEMORY.md）\n", content.Memory),
        };

        return JoinSections(sections);
    }

    /// <summary>
    /// RP 背景区：共用+模式守则 → SOUL → WORLD → USER → MEMORY，各自一条 system 消息；空的节整条跳过。
    /// </summary>
    /// <param name="content">人设文本。</param>
    public static IReadOnlyList<ChatMessage> BuildRoleplayBackground(PersonaContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var messages = new List<ChatMessage>(5);

        var agents = JoinSections(
            ("", content.SharedAgents),
            ("", content.ModeAgents));
        if (agents.Length > 0)
        {
            messages.Add(new ChatMessage(ChatRole.System, agents));
        }

        AddIfPresent(messages, content.Soul);
        AddIfPresent(messages, content.World);
        AddIfPresent(messages, content.User);
        AddIfPresent(messages, content.Memory);

        return messages;
    }

    private static void AddIfPresent(List<ChatMessage> messages, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            messages.Add(new ChatMessage(ChatRole.System, text));
        }
    }

    private static string JoinSections(params (string Header, string? Body)[] sections)
        => string.Join(
            "\n\n",
            sections
                .Where(static s => !string.IsNullOrWhiteSpace(s.Body))
                .Select(static s => s.Header + s.Body!.TrimEnd()));
}
