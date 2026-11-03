using System.Reflection;
using System.Text.Json;

namespace Shoyee.Desktop.Conversations;

/// <summary>读取随程序打包的原始会话快照。接上 RPC 后由 session.messages / lane.messages 替代。</summary>
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
