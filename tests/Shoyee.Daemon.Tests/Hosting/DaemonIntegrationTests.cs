using Xunit;
using Shoyee.Client.Rpc;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Daemon.Configuration;
using Shoyee.Daemon.Hosting;
using Shoyee.Daemon.Tests.TestSupport;

namespace Shoyee.Daemon.Tests.Hosting;

public sealed class DaemonIntegrationTests
{
    private const string WorkspaceJson = """
        {
          "models": {
            "main": { "baseUrl": "http://127.0.0.1:1/v1", "modelId": "main", "apiKeyEnv": "SHOYEE_TEST_MAIN_KEY" },
            "small": { "baseUrl": "http://127.0.0.1:1/v1", "modelId": "small", "apiKeyEnv": "SHOYEE_TEST_SMALL_KEY" }
          },
          "channels": {},
          "proactive": { "heartbeat": { "enabled": false } },
          "tools": {}
        }
        """;

    [Fact]
    public async Task Starts_ServesStatus_RejectsSecondInstance_AndShutsDown()
    {
        using var home = new TempDir();
        var workspace = Path.Combine(home.Path, "workspace");
        Directory.CreateDirectory(workspace);
        var paths = new ShoyeePaths(home.Path);
        File.WriteAllText(paths.ConfigPath, $$"""{ "workspace": "{{workspace}}" }""");
        File.WriteAllText(Path.Combine(workspace, "config.json"), WorkspaceJson);

        Environment.SetEnvironmentVariable("SHOYEE_TEST_MAIN_KEY", "test-key");
        Environment.SetEnvironmentVariable("SHOYEE_TEST_SMALL_KEY", "test-key");
        try
        {
            var run = Task.Run(() => DaemonProgram.RunAsync([], paths, redirectConsoleToFile: false));

            await WaitForSocketAsync(paths.SocketPath);
            Assert.True(File.Exists(paths.LockPath));

            await using (var client = await RpcClient.ConnectAsync(
                             paths.SocketPath,
                             new HelloRequest("test", RpcProtocol.Version, ClientRole.Ctl)))
            {
                var status = await client.CallAsync<StatusDto>(RpcMethods.StatusGet);
                Assert.Equal(workspace, status.Workspace);
                Assert.Contains(status.Lanes, lane => lane.Id == LaneIds.DesktopRoleplay);
                Assert.True(File.Exists(Path.Combine(workspace, "state.db")));

                // 同一个 HOME 上再起一个：锁已被占用，直接返回「已在运行」
                var second = await DaemonProgram.RunAsync([], paths, redirectConsoleToFile: false);
                Assert.Equal(DaemonExitCodes.AlreadyRunning, second);

                await client.CallAsync(RpcMethods.DaemonShutdown, new { });
            }

            var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(DaemonExitCodes.Success, exitCode);
            Assert.False(File.Exists(paths.SocketPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHOYEE_TEST_MAIN_KEY", null);
            Environment.SetEnvironmentVariable("SHOYEE_TEST_SMALL_KEY", null);
        }
    }

    [Fact]
    public async Task TurnSend_BuildsFullGraph_AndFailsCleanlyWhenModelUnreachable()
    {
        using var home = new TempDir();
        var workspace = Path.Combine(home.Path, "workspace");
        Directory.CreateDirectory(workspace);
        var paths = new ShoyeePaths(home.Path);
        File.WriteAllText(paths.ConfigPath, $$"""{ "workspace": "{{workspace}}" }""");
        File.WriteAllText(Path.Combine(workspace, "config.json"), WorkspaceJson);

        Environment.SetEnvironmentVariable("SHOYEE_TEST_MAIN_KEY", "test-key");
        Environment.SetEnvironmentVariable("SHOYEE_TEST_SMALL_KEY", "test-key");
        try
        {
            var run = Task.Run(() => DaemonProgram.RunAsync([], paths, redirectConsoleToFile: false));
            await WaitForSocketAsync(paths.SocketPath);

            await using (var client = await RpcClient.ConnectAsync(
                             paths.SocketPath,
                             new HelloRequest("test", RpcProtocol.Version, ClientRole.DesktopWork)))
            {
                var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.EventReceived += (name, payload) =>
                {
                    if (name == RpcEvents.TurnFailed)
                    {
                        failed.TrySetResult(payload.ToString());
                    }
                };

                // 模型端点不可达也算成功：这里验的是整张 DI 图能建起来、回合失败按事件收束
                var session = await client.CallAsync<SessionDetail>(
                    RpcMethods.SessionCreate,
                    new SessionCreateRequest(Mode.Work, Title: "冒烟"));
                await client.CallAsync<TurnRef>(
                    RpcMethods.TurnSend,
                    new TurnSendRequest("你好", session.Id));

                // 端点不可达：回合以 turn.failed 收束，而不是把整张图卡死
                var payload = await failed.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.False(string.IsNullOrWhiteSpace(payload));
            }

            await using (var ctl = await RpcClient.ConnectAsync(
                             paths.SocketPath,
                             new HelloRequest("test", RpcProtocol.Version, ClientRole.Ctl)))
            {
                await ctl.CallAsync(RpcMethods.DaemonShutdown, new { });
            }

            Assert.Equal(DaemonExitCodes.Success, await run.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHOYEE_TEST_MAIN_KEY", null);
            Environment.SetEnvironmentVariable("SHOYEE_TEST_SMALL_KEY", null);
        }
    }

    [Fact]
    public async Task MissingWorkspace_ReturnsStartupFailure()
    {
        using var home = new TempDir();
        var paths = new ShoyeePaths(home.Path);
        File.WriteAllText(paths.ConfigPath, """{ "workspace": "/definitely/not/here" }""");

        var exitCode = await DaemonProgram.RunAsync([], paths, redirectConsoleToFile: false);

        Assert.Equal(DaemonExitCodes.StartupFailure, exitCode);
        Assert.False(File.Exists(paths.LockPath));
    }

    private static async Task WaitForSocketAsync(string socketPath)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(socketPath))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"socket 未在期限内出现：{socketPath}");
    }
}
