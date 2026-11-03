using Microsoft.Extensions.DependencyInjection;
using Shoyee.Channels.Mattermost;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Xunit;

namespace Shoyee.Channels.Mattermost.Tests;

/// <summary>两个 Mattermost 账号要各自注册成一条独立 Lane，而不是互相覆盖。</summary>
public sealed class MattermostRegistrationTests
{
    [Fact]
    public void AddShoyeeMattermostChannels_RegistersOneAdapterAndSenderPerAccount()
    {
        const string roleplayEnv = "SHOYEE_TEST_MATTERMOST_RP_TOKEN";
        const string workEnv = "SHOYEE_TEST_MATTERMOST_WORK_TOKEN";
        Environment.SetEnvironmentVariable(roleplayEnv, "rp-token");
        Environment.SetEnvironmentVariable(workEnv, "work-token");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IInboundAttachmentStore, FakeAttachmentStore>();
            services.AddShoyeeMattermostChannels(new ChannelAccounts(
                new MattermostAccounts(
                    new MattermostAccount("https://mattermost.example", roleplayEnv),
                    new MattermostAccount("https://mattermost.example", workEnv))));

            using var provider = services.BuildServiceProvider();
            var adapters = provider.GetServices<IChannelAdapter>().ToList();
            var senders = provider.GetServices<IChannelSender>().ToList();

            Assert.Equal(
                new[] { "mattermost:roleplay", "mattermost:work" },
                senders.Select(s => s.LaneId).Order());
            Assert.Equal(
                new[] { "mattermost:roleplay", "mattermost:work" },
                adapters.Select(a => a.LaneId).Order());

            // 同一 Lane 的适配器与发送器必须是同一个实例（连接状态与 bot 身份只存一份）
            foreach (var laneId in new[] { "mattermost:roleplay", "mattermost:work" })
            {
                Assert.Same(
                    adapters.Single(a => a.LaneId == laneId),
                    senders.Single(s => s.LaneId == laneId));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(roleplayEnv, null);
            Environment.SetEnvironmentVariable(workEnv, null);
        }
    }

    private sealed class FakeAttachmentStore : IInboundAttachmentStore
    {
        public Task<InboundAttachment> SaveAsync(
            string fileName,
            string? mime,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new InboundAttachment(
                fileName,
                mime ?? "application/octet-stream",
                $"/tmp/{fileName}",
                data.Length,
                InboundAttachmentKind.File));
    }
}
