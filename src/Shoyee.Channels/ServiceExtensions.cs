using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shoyee.Channels.Attachments;
using Shoyee.Channels.Core;
using Shoyee.Channels.Delivery;
using Shoyee.Contracts.Abstractions;

namespace Shoyee.Channels;

/// <summary>渠道管线注册（供应商由各自的 <c>AddShoyee*Channel</c> 另行注册）。</summary>
public static class ServiceExtensions
{
    /// <summary>
    /// 注册入站管线、投递台账、附件落地与两个后台服务。
    /// 适配器与发送器必须在**构建宿主前**注册（Daemon 组合根按配置决定装哪些）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddShoyeeChannels(this IServiceCollection services)
    {
        services.TryAddSingleton<IInboundAttachmentStore, InboundAttachmentStore>();
        services.TryAddSingleton(_ => new InboundDeduper());
        services.TryAddSingleton<ReliableDelivery>();
        services.TryAddSingleton<IOutboundDelivery>(static sp => sp.GetRequiredService<ReliableDelivery>());
        services.TryAddSingleton<ChannelReplyRouter>();
        services.TryAddSingleton<ChannelApprovalRouter>();
        services.TryAddSingleton<IInboundSink, InboundPipeline>();

        services.AddHostedService<DeliveryRetryService>();
        services.AddHostedService<ChannelHost>();
        return services;
    }
}
