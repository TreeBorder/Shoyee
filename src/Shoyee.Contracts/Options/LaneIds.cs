using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Options;

/// <summary>Lane 的稳定标识拼法（避免各处手拼字符串）。</summary>
public static class LaneIds
{
    /// <summary>桌面 RP 的固定 Lane id。</summary>
    public const string DesktopRoleplay = "desktop:roleplay";

    /// <summary>按渠道与模式拼 Lane id，如 <c>mattermost:work</c>。</summary>
    public static string For(LaneChannel channel, Mode mode)
        => channel == LaneChannel.Desktop
            ? DesktopRoleplay
            : channel.ToString().ToLowerInvariant() + ":" + mode.ToString().ToLowerInvariant();
}
