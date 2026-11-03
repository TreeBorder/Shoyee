using System.Reflection;
using System.Text.Json;

namespace Shoyee.Desktop.Conversations;

/// <summary>
/// 读取随程序打包的**合成示例**会话（不含任何真实对话）。
///
/// 这里只是没连 daemon 时的界面预览：接上 RPC 后由 <c>session.messages</c> / <c>lane.messages</c> 提供真实数据。
/// 不要用真实导出的对话替换这两份 JSON——仓库是公开的，真实数据只应留在工作区。
/// </summary>
internal static class SampleTranscriptSource
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>加载工作模式快照。</summary>
    public static WorkRawFile LoadWork() =>
        Load<WorkRawFile>("Shoyee.Desktop.SampleData.work-sessions.raw.json");

    /// <summary>加载角色模式快照。</summary>
    public static RoleplayRawFile LoadRoleplay() =>
        Load<RoleplayRawFile>("Shoyee.Desktop.SampleData.roleplay-rounds.raw.json");

    private static T Load<T>(string resourceName) where T : new()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
    }
}
