using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.ViewModels.Import;

/// <summary>一个可导入的数据来源。</summary>
/// <param name="Id">持久化路径用的稳定 id。</param>
/// <param name="Mode">导入到哪种模式。</param>
/// <param name="Title">来源名。</param>
/// <param name="Description">支持的路径说明。</param>
/// <param name="Placeholder">输入框示例。</param>
/// <param name="WorkImport">工作模式导入函数；角色来源为 null。</param>
/// <param name="RoleplayImport">角色模式导入函数；工作来源为 null。</param>
internal sealed record ImportSourceDescriptor(
    string Id,
    ImportMode Mode,
    string Title,
    string Description,
    string Placeholder,
    Func<string, IReadOnlyList<SessionItem>>? WorkImport = null,
    Func<string, RoleplayContent>? RoleplayImport = null);

/// <summary>
/// 导入来源目录。以后接 Hermes Agent 之类的来源，只需要在这里加一条，
/// 页面会按 Mode 自动分到「工作模式」或「角色模式」。
/// </summary>
internal static class ImportCatalog
{
    /// <summary>全部来源。</summary>
    public static IReadOnlyList<ImportSourceDescriptor> All { get; } =
    [
        new(
            Id: "secretary",
            Mode: ImportMode.Work,
            Title: "Secretary",
            Description: "工作会话：项目根目录（含 state.db）、state.db、sessions 目录或单个会话 JSON。",
            Placeholder: "例如 ~/Secretary",
            WorkImport: SecretaryHistoryImporter.Import),
        new(
            Id: "harem",
            Mode: ImportMode.Roleplay,
            Title: "Harem",
            Description: "角色轮次：工作区（含 .history）、.history 目录、会话目录或 rounds.json；默认取最近更新的会话。",
            Placeholder: "例如 ~/Harem/border",
            RoleplayImport: HaremHistoryImporter.Import),
    ];
}
