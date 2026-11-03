using Shoyee.Contracts.Domain;

namespace Shoyee.Sessions.Persona;

/// <summary>
/// 工作区人设文件访问：<c>SOUL.md</c> / <c>USER.md</c> / <c>MEMORY.md</c> / <c>AGENTS.shared.md</c> /
/// <c>modes/{work,roleplay}/AGENTS.md</c> / <c>modes/roleplay/WORLD.md</c>。
///
/// **不缓存**：每次 <see cref="Load"/> 都重新读盘。工作模式在会话创建时读一次并写进 session，
/// 之后不再调用；RP 每轮调用，因此文件改动下一轮即可见。
/// </summary>
public sealed class PersonaFiles
{
    /// <summary>共用行为守则文件名。</summary>
    public const string SharedAgentsFileName = "AGENTS.shared.md";

    /// <summary>人设文件名。</summary>
    public const string SoulFileName = "SOUL.md";

    /// <summary>用户画像文件名。</summary>
    public const string UserFileName = "USER.md";

    /// <summary>长期记忆文件名。</summary>
    public const string MemoryFileName = "MEMORY.md";

    /// <summary>世界观相对路径（仅 RP）。</summary>
    public static readonly string WorldRelativePath = Path.Combine("modes", "roleplay", "WORLD.md");

    private readonly string _root;

    /// <summary>创建工作区文件访问。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    public PersonaFiles(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = workspaceRoot;
    }

    /// <summary>工作区根目录。</summary>
    public string Root => _root;

    /// <summary>共用行为守则路径。</summary>
    public string SharedAgentsPath => Path.Combine(_root, SharedAgentsFileName);

    /// <summary>人设路径。</summary>
    public string SoulPath => Path.Combine(_root, SoulFileName);

    /// <summary>用户画像路径。</summary>
    public string UserPath => Path.Combine(_root, UserFileName);

    /// <summary>长期记忆路径。</summary>
    public string MemoryPath => Path.Combine(_root, MemoryFileName);

    /// <summary>世界观路径（仅 RP 使用）。</summary>
    public string WorldPath => Path.Combine(_root, WorldRelativePath);

    /// <summary>模式子目录名（磁盘上的文件夹）。</summary>
    /// <param name="mode">模式。</param>
    public static string ModeFolder(Mode mode) => mode switch
    {
        Mode.Work => "work",
        Mode.Roleplay => "roleplay",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知模式。"),
    };

    /// <summary>模式守则的相对路径。</summary>
    /// <param name="mode">模式。</param>
    public static string ModeAgentsRelativePath(Mode mode)
        => Path.Combine("modes", ModeFolder(mode), "AGENTS.md");

    /// <summary>模式守则路径。</summary>
    /// <param name="mode">模式。</param>
    public string ModeAgentsPath(Mode mode) => Path.Combine(_root, ModeAgentsRelativePath(mode));

    /// <summary>
    /// 读取该模式用到的全部人设文件。缺失文件记空串；工作模式不读 <c>WORLD.md</c>。
    /// </summary>
    /// <param name="mode">模式。</param>
    public PersonaContent Load(Mode mode) => new(
        ReadIfExists(SharedAgentsPath),
        ReadIfExists(ModeAgentsPath(mode)),
        ReadIfExists(SoulPath),
        mode == Mode.Roleplay ? ReadIfExists(WorldPath) : string.Empty,
        ReadIfExists(MemoryPath),
        ReadIfExists(UserPath));

    private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}
