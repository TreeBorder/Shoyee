using Shoyee.Client.Rpc;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;
using Xunit;

namespace Shoyee.Gateway.Tests.Rpc;

/// <summary><c>workspace.paths</c>：只返回 daemon 自己拼的固定路径。</summary>
public sealed class WorkspaceRpcTests
{
    [Fact]
    public async Task WorkspacePaths_WorkDesktop_ReturnsFixedPaths()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<WorkspacePathsDto>(RpcMethods.WorkspacePaths);

        Assert.Equal(bed.Root, result.Workspace);
        Assert.Equal(Path.Combine(bed.Root, "config.json"), result.ConfigFile);
        Assert.Equal(Path.Combine(bed.Root, "SOUL.md"), result.Prompts.Soul);
        Assert.Equal(
            Path.Combine(bed.Root, "modes", "roleplay", "WORLD.md"),
            result.Prompts.RoleplayWorld);
        Assert.Equal(
            Path.Combine(bed.Root, ".heartbeat", "prompts.json"),
            result.Heartbeat.Prompts);
        Assert.Equal(
            Path.Combine(bed.Paths.AppDataRoot, "logs", "daemon.log"),
            result.DaemonLog);
        Assert.Equal(Path.Combine(bed.Paths.AppDataRoot, "avatars"), result.Avatars);
    }

    [Fact]
    public async Task WorkspacePaths_RoleplayDesktop_Allowed()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var result = await client.CallAsync<WorkspacePathsDto>(RpcMethods.WorkspacePaths);

        Assert.Equal(bed.Root, result.Workspace);
    }

    [Fact]
    public async Task WorkspacePaths_Menu_IsRejected()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.Menu);

        var ex = await Assert.ThrowsAsync<RpcClientException>(() =>
            client.CallAsync<WorkspacePathsDto>(RpcMethods.WorkspacePaths));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
    }
}
