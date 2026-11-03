using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Options;

namespace Shoyee.Channels.Mattermost;

/// <summary>Mattermost 渠道注册：一个账号一条 Lane（RP / 工作各一个 token）。</summary>
public static class ServiceExtensions
{
    /// <summary>按 <see cref="ChannelAccounts.Mattermost"/> 注册两个账号（缺哪个就少一条）。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="channels">渠道账号配置。</param>
    public static IServiceCollection AddShoyeeMattermostChannels(
        this IServiceCollection services,
        ChannelAccounts channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Mattermost is not { } mattermost)
        {
            return services;
        }

        services.AddShoyeeMattermostChannel(mattermost.Roleplay, LaneIds.For(LaneChannel.Mattermost, Mode.Roleplay));
        services.AddShoyeeMattermostChannel(mattermost.Work, LaneIds.For(LaneChannel.Mattermost, Mode.Work));
        return services;
    }

    /// <summary>注册单个 Mattermost 账号。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="account">账号配置。</param>
    /// <param name="laneId">Lane id（同时作为发送器标识）。</param>
    public static IServiceCollection AddShoyeeMattermostChannel(
        this IServiceCollection services,
        MattermostAccount account,
        string laneId)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);

        // 同一个实例同时充当入站适配器和出站发送器：连接状态与 bot 身份只存一份。
        // 不能用 AddSingleton(sp => new MattermostChannel(...)) 再按具体类型解析——两次注册会互相覆盖，
        // 两个账号最后都拿到后创建的那个实例（RP 账号被工作账号顶掉，ReliableDelivery 直接撞重复 key）。
        // 账号与 laneId 不是 DI 服务，先打成 keyed options；适配器本身再按同一把 key 注册，容器直接构造。
        services.AddKeyedSingleton(laneId, new MattermostChannelOptions(account, laneId));
        services.AddKeyedSingleton<MattermostChannel>(laneId);

        // 上层仍按未 keyed 的 IEnumerable<IChannelAdapter> / <IChannelSender> 枚举，这里转一道。
        services.AddSingleton<IChannelAdapter>(
            sp => sp.GetRequiredKeyedService<MattermostChannel>(laneId));
        services.AddSingleton<IChannelSender>(
            sp => sp.GetRequiredKeyedService<MattermostChannel>(laneId));
        return services;
    }
}
