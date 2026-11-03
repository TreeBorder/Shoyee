using System.Text.Json;
using Shoyee.Client.Rpc;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;
using Xunit;

namespace Shoyee.Gateway.Tests.Rpc;

/// <summary>运行时视图：工具清单、MCP、状态写入、按会话用量。</summary>
public sealed class RuntimeRpcTests
{
    [Fact]
    public async Task ToolsList_FiltersByScopeAndQuery()
    {
        var catalog = new FakeToolCatalog([
            new ToolInfo("file_access_read", "读文件", "all", true, false, false, "builtin"),
            new ToolInfo("terminal", "执行命令", "work", true, true, true, "builtin"),
            new ToolInfo("get_states", "读状态", "roleplay", false, false, false, "builtin"),
        ]);
        await using var bed = await GatewayTestBed.StartAsync(toolCatalog: catalog, mcp: new FakeMcpControl());
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var work = await client.CallAsync<ToolListResult>(RpcMethods.ToolsList, new { scope = "work" });
        var filtered = await client.CallAsync<ToolListResult>(RpcMethods.ToolsList, new { query = "命令" });

        Assert.Equal(2, work.Items.Count);
        Assert.DoesNotContain(work.Items, item => item.Name == "get_states");
        var only = Assert.Single(filtered.Items);
        Assert.Equal("terminal", only.Name);
        Assert.True(only.RequiresApproval);
    }

    [Fact]
    public async Task ToolsList_RoleplayDesktop_IsRejected()
    {
        await using var bed = await GatewayTestBed.StartAsync(toolCatalog: new FakeToolCatalog([]));
        await using var client = await bed.ConnectAsync(ClientRole.DesktopRoleplay, LaneIds.DesktopRoleplay);

        var ex = await Assert.ThrowsAsync<RpcClientException>(() =>
            client.CallAsync<ToolListResult>(RpcMethods.ToolsList));

        Assert.Equal(RpcErrors.UnauthorizedRole, ex.Code);
    }

    [Fact]
    public async Task McpStatus_ReturnsServers()
    {
        var mcp = new FakeMcpControl
        {
            Status = [new McpServerStatus("squad", "http://127.0.0.1:1", true, false, [], "连接失败")],
        };
        await using var bed = await GatewayTestBed.StartAsync(mcp: mcp);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<McpStatusResult>(RpcMethods.McpStatus);

        var server = Assert.Single(result.Servers);
        Assert.Equal("squad", server.Name);
        Assert.Equal("连接失败", server.Error);
    }

    [Fact]
    public async Task McpReload_DelegatesAndReportsRestart()
    {
        var mcp = new FakeMcpControl
        {
            ReloadResult = new McpReloadResult(
                [new McpServerStatus("squad", "http://127.0.0.1:1", true, true, ["squad_list"], null)],
                RestartRequired: true),
        };
        await using var bed = await GatewayTestBed.StartAsync(mcp: mcp);
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<McpReloadResult>(RpcMethods.McpReload);

        Assert.Equal(1, mcp.ReloadCalls);
        Assert.True(result.RestartRequired);
        Assert.Equal(["squad_list"], Assert.Single(result.Servers).Tools);
    }

    [Fact]
    public async Task StateSet_UpdatesValueAndPublishesEvent()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var events = new List<(string Name, JsonElement Payload)>();
        var received = new TaskCompletionSource();
        client.EventReceived += (name, payload) =>
        {
            if (name != RpcEvents.StateUpdated)
            {
                return;
            }

            lock (events)
            {
                events.Add((name, payload));
            }

            received.TrySetResult();
        };

        var result = await client.CallAsync<StateDto>(RpcMethods.StateSet, new { key = "mood", value = "开心" });

        await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("开心", result.Value);
        (string, JsonElement) item;
        lock (events)
        {
            item = Assert.Single(events);
        }

        Assert.Equal("开心", item.Item2.GetProperty("value").GetString());
    }

    [Fact]
    public async Task StateSet_UnknownKey_IsNotFound()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(() =>
            client.CallAsync<StateDto>(RpcMethods.StateSet, new { key = "missing", value = "x" }));

        Assert.Equal(RpcErrors.NotFound, ex.Code);
    }

    [Fact]
    public async Task UsageGet_DefaultsToToday()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var result = await client.CallAsync<UsageGetResult>(RpcMethods.UsageGet);

        Assert.Equal(2, result.Summary.Calls);
        Assert.Equal(100, result.Summary.InputTokens);
        Assert.True(result.To >= result.From);
        Assert.Equal(TimeSpan.Zero, result.From.Offset);
    }

    [Fact]
    public async Task UsageGet_InvalidRange_IsInvalidParams()
    {
        await using var bed = await GatewayTestBed.StartAsync();
        await using var client = await bed.ConnectAsync(ClientRole.DesktopWork);

        var ex = await Assert.ThrowsAsync<RpcClientException>(() => client.CallAsync<UsageGetResult>(
            RpcMethods.UsageGet,
            new { from = "2026-10-30T10:00:00Z", to = "2026-10-30T09:00:00Z" }));

        Assert.Equal(RpcErrors.InvalidParams, ex.Code);
    }
}

internal sealed class FakeToolCatalog(IReadOnlyList<ToolInfo> items) : IToolCatalog
{
    public IReadOnlyList<ToolInfo> List() => items;
}

internal sealed class FakeMcpControl : IMcpControl
{
    public IReadOnlyList<McpServerStatus> Status { get; set; } = [];

    public McpReloadResult ReloadResult { get; set; } = new([], RestartRequired: false);

    public int ReloadCalls { get; private set; }

    public Task<McpReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        ReloadCalls++;
        Status = ReloadResult.Servers;
        return Task.FromResult(ReloadResult);
    }
}
