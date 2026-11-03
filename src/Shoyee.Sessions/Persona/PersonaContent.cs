namespace Shoyee.Sessions.Persona;

/// <summary>
/// 一次读取得到的人设文本集合。所有字段都是原文，**缺失文件记空串**（不是 null），
/// 拼装时按空串跳过该节——工作区文件可以缺，缺了只是少注入一节，不让会话创建失败。
/// </summary>
/// <param name="SharedAgents">共用行为守则（<c>AGENTS.shared.md</c>）。</param>
/// <param name="ModeAgents">模式守则（<c>modes/{work|roleplay}/AGENTS.md</c>）。</param>
/// <param name="Soul">人设（<c>SOUL.md</c>）。</param>
/// <param name="World">世界观（<c>modes/roleplay/WORLD.md</c>，仅 RP；工作模式为空串）。</param>
/// <param name="Memory">长期记忆（<c>MEMORY.md</c>）。</param>
/// <param name="User">用户画像（<c>USER.md</c>）。</param>
public sealed record PersonaContent(
    string SharedAgents,
    string ModeAgents,
    string Soul,
    string World,
    string Memory,
    string User);
