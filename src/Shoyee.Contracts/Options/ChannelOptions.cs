namespace Shoyee.Contracts.Options;

/// <summary>所有渠道账号。每个账号绑定一个模式。</summary>
/// <param name="Mattermost">Mattermost 双 token（RP + 工作）。</param>
/// <param name="WeChat">微信（只做工作模式）。</param>
public sealed record ChannelAccounts(MattermostAccounts? Mattermost = null, WeChatAccount? WeChat = null);

/// <summary>Mattermost 的两个独立账号。</summary>
/// <param name="Roleplay">RP 账号。</param>
/// <param name="Work">工作账号。</param>
public sealed record MattermostAccounts(MattermostAccount Roleplay, MattermostAccount Work);

/// <summary>一个 Mattermost 机器人账号。</summary>
/// <param name="BaseUrl">服务器地址。</param>
/// <param name="TokenEnv">bot token 的环境变量名。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="AllowedUsers">允许的用户 id 白名单；空 = 不限（单用户自用场景）。</param>
/// <param name="RequireMention">频道里是否必须 @bot 才响应（私聊免）。</param>
public sealed record MattermostAccount(
    string BaseUrl,
    string TokenEnv,
    string? DisplayName = null,
    IReadOnlyList<string>? AllowedUsers = null,
    bool RequireMention = true);

/// <summary>微信账号（只做工作模式，复用现有 token）。</summary>
/// <param name="TokenEnv">token 的环境变量名。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="BaseUrl">iLink 接入点。</param>
/// <param name="AccountId">bot 账号 id（用于识别自发消息；非密钥）。</param>
/// <param name="AllowedUsers">允许的用户 id 白名单；空 = 不限。</param>
/// <param name="CdnBaseUrl">媒体 CDN 接入点（可空 = 官方默认）。</param>
public sealed record WeChatAccount(
    string TokenEnv,
    string? DisplayName = null,
    string BaseUrl = "https://ilinkai.weixin.qq.com",
    string? AccountId = null,
    IReadOnlyList<string>? AllowedUsers = null,
    string? CdnBaseUrl = null);
