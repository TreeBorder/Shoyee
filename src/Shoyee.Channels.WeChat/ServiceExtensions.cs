using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Options;

namespace Shoyee.Channels.WeChat;

/// <summary>微信渠道注册（只做工作模式，一条 Lane）。</summary>
public static class ServiceExtensions
{
    /// <summary>按配置注册微信适配器；未配置就什么都不做。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="channels">渠道账号配置。</param>
    public static IServiceCollection AddShoyeeWeChatChannel(
        this IServiceCollection services,
        ChannelAccounts channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.WeChat is not { } account)
        {
            return services;
        }

        var laneId = LaneIds.For(LaneChannel.WeChat, Mode.Work);
        services.AddSingleton(sp => new WeChatChannel(
            account,
            laneId,
            sp.GetRequiredService<WorkspaceConfig>().Root,
            sp.GetRequiredService<IInboundAttachmentStore>(),
            sp.GetRequiredService<ILogger<WeChatChannel>>()));
        services.AddSingleton<IChannelAdapter>(sp => sp.GetRequiredService<WeChatChannel>());
        services.AddSingleton<IChannelSender>(sp => sp.GetRequiredService<WeChatChannel>());
        return services;
    }
}
