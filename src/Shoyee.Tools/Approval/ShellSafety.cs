using System.Text.RegularExpressions;

namespace Shoyee.Tools.Approval;

/// <summary>
/// 命令危险性分类——**审批层分类，不是执行拦截**：命中危险模式 → 浮出审批；未命中 → 自动放行直接跑。
/// 宁可误报多问一句，不可漏报放行破坏性命令（模式可绕过不是安全边界，真正的边界是审批在环）。
/// </summary>
public static class ShellSafety
{
    private static readonly Regex[] DangerPatterns =
    [
        // 删除/破坏文件系统
        new(@"\brm\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@"\b(unlink|shred|truncate)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@"\bmkfs(\.\w+)?\b|\bdd\b[^|;&]*\bof=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@">\s*/dev/(disk|rdisk|sd|nvme|hd)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),

        // 权限/属主/提权
        new(@"\bsudo\b|\bsu\s|\bchmod\b|\bchown\b|\bchgrp\b|\bchflags\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),

        // 进程与系统管理
        new(@"\b(kill|killall|pkill|shutdown|reboot|halt|poweroff)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@"\b(launchctl|systemctl|service)\b|\bcrontab\b[^|;&]*-r", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@"\b(diskutil|mount|umount|mkswap|fdisk|parted|newfs)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),

        // 管道执行远程脚本 / fork 炸弹 / eval
        new(@"(curl|wget|fetch)[^|;&|]*\|\s*(sudo\s+)?(ba|z)?sh\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@":\s*\(\s*\)\s*\{|\beval\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),

        // 不可逆的版本/发布操作
        new(@"\bgit\s+(push\b[^|;&]*(--force\b|-f\b)|reset\s+--hard|clean\s+-[a-z]*f)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@"\b(npm|pnpm|yarn|twine|cargo|dotnet)\b[^|;&]*\b(publish|nuget push)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
    ];

    /// <summary>是否危险命令（命中任一危险模式；空命令按危险处理）。</summary>
    public static bool IsDangerous(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return true;
        }

        foreach (var pattern in DangerPatterns)
        {
            if (pattern.IsMatch(command))
            {
                return true;
            }
        }

        return false;
    }
}
