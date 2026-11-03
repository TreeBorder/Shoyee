using Shoyee.Contracts.Options;

namespace Shoyee.Channels.Mattermost;

/// <summary>
/// 一个 Mattermost 账号对应的适配器参数。账号与 Lane 是配置值、不在 DI 里，
/// 打进 options 后按 laneId keyed 注册，适配器就能由容器直接构造，不用手写工厂。
/// </summary>
/// <param name="Account">账号配置（token 只写环境变量名）。</param>
/// <param name="LaneId">该账号对应的 Lane id。</param>
public sealed record MattermostChannelOptions(MattermostAccount Account, string LaneId);
