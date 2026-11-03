using Shoyee.Client.Rpc;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;
using Xunit;

namespace Shoyee.Gateway.Tests.Rpc;

public sealed class NotifyRpcTests
{
    [Fact]
    public async Task NotifyTest_WorkDesktop_SendsAndReportsProvider()
    {
        var pusher = new RecordingPusher();
        await using var bed = await GatewayTestBed.StartAsync(notifications: pusher);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<NotifyTestResult>(
            RpcMethods.NotifyTest,
            new NotifyTestRequest("标题", "正文"));

        Assert.True(result.Success);
        Assert.Equal("system", result.Provider);
        Assert.Null(result.Error);
        Assert.Equal(("标题", "正文"), pusher.Last);
    }

    [Fact]
    public async Task NotifyTest_Failure_IsReturnedAsResultNotRpcError()
    {
        var pusher = new RecordingPusher { Error = "通知权限被拒绝" };
        await using var bed = await GatewayTestBed.StartAsync(notifications: pusher);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<NotifyTestResult>(RpcMethods.NotifyTest, new { });

        Assert.False(result.Success);
        Assert.Contains("通知权限被拒绝", result.Error);
        Assert.Equal("Shoyee 通知测试", pusher.Last.Title);
    }

    [Fact]
    public async Task NotifyTest_MenuRole_IsAllowed()
    {
        var pusher = new RecordingPusher();
        await using var bed = await GatewayTestBed.StartAsync(notifications: pusher);
        await using var client = await bed.ConnectAsync(ClientRole.Menu);

        var result = await client.CallAsync<NotifyTestResult>(RpcMethods.NotifyTest, new { });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task NotifyTest_RoleplayDesktop_IsUnauthorized()
    {
        await using var bed = await GatewayTestBed.StartAsync(notifications: new RecordingPusher());
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(
            () => client.CallAsync(RpcMethods.NotifyTest, new { }));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
    }

    private sealed class RecordingPusher : INotificationPusher
    {
        public string? Error { get; init; }

        public (string Title, string Body) Last { get; private set; }

        public Task PushAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            Last = (title, body);
            return Error is null ? Task.CompletedTask : Task.FromException(new InvalidOperationException(Error));
        }
    }
}
