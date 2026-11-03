using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Shoyee.Desktop.Theming;

/// <summary>
/// 用户 / 智能体头像资源：把文件或内置图片写进 <see cref="Application.Resources"/>，
/// 消息模板和设置页统一用 <c>DynamicResource</c> 取，改头像时不需要重建消息列表。
/// </summary>
public static class AvatarResources
{
    /// <summary>用户头像资源键。</summary>
    public const string UserKey = "UserAvatar";

    /// <summary>智能体头像资源键。</summary>
    public const string AgentKey = "AgentAvatar";

    private const string DefaultUser = "avares://Shoyee.Desktop/Assets/user-avatar.png";
    private const string DefaultAgent = "avares://Shoyee.Desktop/Assets/avatar.png";

    private static Bitmap? _defaultUser;
    private static Bitmap? _defaultAgent;

    /// <summary>应用用户与智能体头像；路径为空或读不出来时回落内置图片。</summary>
    /// <param name="userPath">用户头像绝对路径。</param>
    /// <param name="agentPath">智能体头像绝对路径。</param>
    public static void Apply(string? userPath, string? agentPath)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.Resources[UserKey] = Resolve(userPath, isUser: true);
        app.Resources[AgentKey] = Resolve(agentPath, isUser: false);
    }

    private static Bitmap Resolve(string? path, bool isUser)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                return new Bitmap(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 图片坏了就回落默认，不拦界面
            }
        }

        if (isUser)
        {
            return _defaultUser ??= LoadAsset(DefaultUser);
        }

        return _defaultAgent ??= LoadAsset(DefaultAgent);
    }

    private static Bitmap LoadAsset(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        return new Bitmap(stream);
    }
}
