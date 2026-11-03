using Shoyee.Client.Rpc;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;
using Xunit;

namespace Shoyee.Gateway.Tests.Rpc;

/// <summary><c>config.reload</c> 的数据面与角色约束。</summary>
public sealed class ConfigRpcTests
{
    [Fact]
    public async Task ConfigReload_WorkDesktop_Delegates()
    {
        var fake = new FakeConfigReloader(DefaultConfig()) { Result = new(["models", "tools"], ["channels"]) };
        await using var bed = await GatewayTestBed.StartAsync(configReloader: fake);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<ConfigReloadResult>(RpcMethods.ConfigReload);

        Assert.Equal(1, fake.ReloadCalls);
        Assert.Equal(["models", "tools"], result.Changed);
        Assert.Equal(["channels"], result.RestartRequired);
    }

    [Fact]
    public async Task ConfigReload_Ctl_Allowed()
    {
        var fake = new FakeConfigReloader(DefaultConfig());
        await using var bed = await GatewayTestBed.StartAsync(configReloader: fake);
        await using var client = await bed.ConnectAsync(ClientRole.Ctl);

        await client.CallAsync<ConfigReloadResult>(RpcMethods.ConfigReload);

        Assert.Equal(1, fake.ReloadCalls);
    }

    [Fact]
    public async Task ConfigReload_RoleplayDesktop_IsRejected()
    {
        var fake = new FakeConfigReloader(DefaultConfig());
        await using var bed = await GatewayTestBed.StartAsync(configReloader: fake);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(() =>
            client.CallAsync<ConfigReloadResult>(RpcMethods.ConfigReload));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
        Assert.Equal(0, fake.ReloadCalls);
    }

    [Fact]
    public async Task ConfigGet_ReturnsReloaderCurrent()
    {
        var current = DefaultConfig() with { Tools = new ToolOptions(Disabled: ["terminal"]) };
        var fake = new FakeConfigReloader(current);
        await using var bed = await GatewayTestBed.StartAsync(configReloader: fake);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<WorkspaceConfig>(RpcMethods.ConfigGet);

        Assert.Equal(["terminal"], result.Tools.Disabled);
    }

    [Fact]
    public async Task ConfigSet_WorkDesktop_DelegatesPatch()
    {
        var fake = new FakeConfigReloader(DefaultConfig())
        {
            SetResult = new(["shell.denyPatterns"], ["shell"], "/tmp/shoyee-config-rpc/config.json", ["提醒"]),
        };
        await using var bed = await GatewayTestBed.StartAsync(configReloader: fake);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<ConfigSetResult>(
            RpcMethods.ConfigSet,
            new { patch = new { shell = new { denyPatterns = new[] { "shutdown" } } } });

        Assert.Equal(1, fake.SetCalls);
        Assert.NotNull(fake.LastSetRequest?.Patch);
        Assert.Null(fake.LastSetRequest!.Raw);
        Assert.Equal(["shell.denyPatterns"], result.Changed);
        Assert.Equal(["shell"], result.RestartRequired);
        Assert.Equal(["提醒"], result.Warnings);
    }

    [Fact]
    public async Task ConfigSet_Ctl_Allowed()
    {
        var fake = new FakeConfigReloader(DefaultConfig());
        await using var bed = await GatewayTestBed.StartAsync(configReloader: fake);
        await using var client = await bed.ConnectAsync(ClientRole.Ctl);

        await client.CallAsync<ConfigSetResult>(RpcMethods.ConfigSet, new { raw = "{}" });

        Assert.Equal(1, fake.SetCalls);
        Assert.Equal("{}", fake.LastSetRequest!.Raw);
    }

    [Fact]
    public async Task ConfigSet_RoleplayDesktop_IsRejected()
    {
        var fake = new FakeConfigReloader(DefaultConfig());
        await using var bed = await GatewayTestBed.StartAsync(configReloader: fake);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(() =>
            client.CallAsync<ConfigSetResult>(RpcMethods.ConfigSet, new { raw = "{}" }));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
        Assert.Equal(0, fake.SetCalls);
    }

    [Fact]
    public async Task ConfigSet_NotWired_IsInternal()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(() =>
            client.CallAsync<ConfigSetResult>(RpcMethods.ConfigSet, new { raw = "{}" }));

        Assert.Equal(RpcErrors.Internal, ex.Code);
    }

    private static WorkspaceConfig DefaultConfig()
        => new(
            "/tmp/shoyee-config-rpc",
            new ModelProfiles(
                new ModelEndpoint("http://127.0.0.1:1/v1", "main", "MAIN_KEY"),
                new ModelEndpoint("http://127.0.0.1:1/v1", "small", "SMALL_KEY")),
            new ChannelAccounts(),
            new ProactiveOptions(new HeartbeatOptions()),
            new ToolOptions());
}
