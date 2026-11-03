using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shoyee.Agent;
using Shoyee.Channels;
using Shoyee.Channels.Core;
using Shoyee.Channels.Mattermost;
using Shoyee.Channels.WeChat;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Daemon.Configuration;
using Shoyee.Daemon.Lanes;
using Shoyee.Daemon.Photo;
using Shoyee.Gateway;
using Shoyee.Gateway.Rpc;
using Shoyee.Gateway.Subscriptions;
using Shoyee.Handlers;
using Shoyee.Memory;
using Shoyee.Notes.Obsidian;
using Shoyee.Notify.Email;
using Shoyee.Notify.ServerChan;
using Shoyee.Notify.System;
using Shoyee.Photo;
using Shoyee.Photo.Comfy;
using Shoyee.Proactive;
using Shoyee.Proactive.Heartbeat;
using Shoyee.Sessions;
using Shoyee.Sessions.Persona;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Sqlite;
using Shoyee.Tools;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Mcp;
using Shoyee.Voice.Apple;
using Shoyee.Voice.Mimo;
using Shoyee.Web.DeepSeek;

namespace Shoyee.Daemon.Hosting;

/// <summary>daemon 的启动流程：配置 → 单实例 → 装配 → 初始化 → 跑宿主。</summary>
public static class DaemonProgram
{
    /// <summary>运行 daemon，返回进程退出码。</summary>
    /// <param name="args">命令行参数（交给 Host 解析日志级别等）。</param>
    /// <param name="paths">路径集合（测试可注入）。</param>
    /// <param name="redirectConsoleToFile">
    /// 输出被重定向时是否改写到 <c>logs/daemon.log</c>（进程内测试传 false，避免抢走测试进程的 Console）。
    /// </param>
    public static async Task<int> RunAsync(
        string[] args,
        ShoyeePaths? paths = null,
        bool redirectConsoleToFile = true)
    {
        paths ??= ShoyeePaths.Default;

        DaemonConfig daemonConfig;
        WorkspaceConfig workspaceConfig;
        try
        {
            paths.EnsureRunDirectory();
            daemonConfig = DaemonConfigLoader.Load(paths);
            // 工作区 .env 补齐密钥等环境变量；进程里已有的优先
            WorkspaceEnvironment.Apply(daemonConfig.Workspace);
            workspaceConfig = WorkspaceConfigLoader.Load(daemonConfig.Workspace);
        }
        catch (DaemonStartupException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return DaemonExitCodes.StartupFailure;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"准备运行目录失败：{paths.AppDataRoot}（{ex.Message}）");
            return DaemonExitCodes.StartupFailure;
        }

        using var instance = SingleInstanceLock.TryAcquire(paths.LockPath);
        if (instance is null)
        {
            Console.Error.WriteLine($"Shoyee daemon 已在运行（锁：{paths.LockPath}），不抢占。");
            return DaemonExitCodes.AlreadyRunning;
        }

        if (redirectConsoleToFile)
        {
            RedirectConsoleToLogFile(paths);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var dbPath = workspaceConfig.ResolveDatabasePath();
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));

        var configStore = new WorkspaceConfigStore(workspaceConfig);
        builder.Services.AddSingleton(configStore);
        builder.Services.AddSingleton(static sp => sp.GetRequiredService<WorkspaceConfigStore>().Current);
        builder.Services.AddShoyeeSqliteStorage(workspaceConfig);
        builder.Services.AddShoyeeSessions();
        builder.Services.AddShoyeeMemory();
        builder.Services.AddShoyeeTools();

        // 网页能力按配置选供应商；未知供应商直接拒绝启动，别让工具静默消失
        var webProvider = (workspaceConfig.Web?.Provider ?? "deepseek").Trim();
        if (!string.Equals(webProvider, "deepseek", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"未知的 web provider：{webProvider}（当前支持：deepseek）。");
            return DaemonExitCodes.StartupFailure;
        }

        builder.Services.AddShoyeeWebDeepSeek(workspaceConfig.Web);

        // 笔记库同样按配置选供应商（未配置 = 不注册，notes_search 不挂载）
        var notesProvider = (workspaceConfig.Notes?.Provider ?? "obsidian").Trim();
        if (!string.Equals(notesProvider, "obsidian", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"未知的 notes provider：{notesProvider}（当前支持：obsidian）。");
            return DaemonExitCodes.StartupFailure;
        }

        builder.Services.AddShoyeeObsidianNotes(workspaceConfig.Notes);

        // 语音：合成与转写各自选供应商（未配置 = 不注册，对应工具不挂载）。
        // 未知 key 直接拒绝启动，理由同 web/notes：别让工具静默消失。
        if (workspaceConfig.Voice is { } voice)
        {
            var synthesis = voice.ResolveSynthesisProvider();
            var transcription = voice.ResolveTranscriptionProvider();

            if (!IsDisabled(synthesis))
            {
                if (!string.Equals(synthesis, "mimo", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"未知的语音合成 provider：{synthesis}（当前支持：mimo；关闭该方向写 none）。");
                    return DaemonExitCodes.StartupFailure;
                }

                builder.Services.AddShoyeeMimoSynthesis(voice);
            }

            if (!IsDisabled(transcription))
            {
                switch (transcription.ToLowerInvariant())
                {
                    case "mimo":
                        builder.Services.AddShoyeeMimoTranscription(voice);
                        break;
                    case "apple":
                        builder.Services.AddShoyeeAppleTranscription(voice);
                        break;
                    default:
                        Console.Error.WriteLine(
                            $"未知的语音转写 provider：{transcription}（当前支持：mimo、apple；关闭该方向写 none）。");
                        return DaemonExitCodes.StartupFailure;
                }
            }
        }

        // 图片：按配置选供应商（未配置 = 不注册，图片工具不挂载）。
        // 未知 key 直接拒绝启动，理由同 web/notes：别让工具静默消失。
        if (workspaceConfig.Photo is { } photo)
        {
            var photoProvider = (photo.Provider ?? "comfy").Trim();
            if (!string.Equals(photoProvider, "comfy", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"未知的 photo provider：{photoProvider}（当前支持：comfy）。");
                return DaemonExitCodes.StartupFailure;
            }

            builder.Services.AddShoyeeComfyPhoto(photo);
            builder.Services.AddShoyeePhoto(photo);
            builder.Services.AddSingleton<IPhotoDelivery, LanePhotoDelivery>();
        }

        // 通知：单选渠道，默认 macOS 系统通知；none = 关闭（cron 的 push 会在触发时报错）。
        // 未知 key 直接拒绝启动，理由同 web/notes：别让通知静默消失。
        var notifyProvider = (workspaceConfig.Notify?.Provider ?? SystemNotificationPusher.ProviderKey).Trim();
        if (IsDisabled(notifyProvider))
        {
            builder.Services.AddSingleton<INotificationPusher, DisabledNotificationPusher>();
        }
        else
        {
            switch (notifyProvider.ToLowerInvariant())
            {
                case SystemNotificationPusher.ProviderKey:
                    builder.Services.AddShoyeeSystemNotify(workspaceConfig.Notify);
                    break;
                case ServerChanPusher.ProviderKey:
                    builder.Services.AddShoyeeServerChanNotify(workspaceConfig.Notify);
                    break;
                case SmtpEmailPusher.ProviderKey:
                    builder.Services.AddShoyeeEmailNotify(workspaceConfig.Notify);
                    break;
                default:
                    Console.Error.WriteLine(
                        $"未知的 notify provider：{notifyProvider}（当前支持：system、serverchan、email；关闭写 none）。");
                    return DaemonExitCodes.StartupFailure;
            }
        }

        // MCP 是异步连接：必须在 host 构建前收罗完，单例工具面才不会缺项。
        // 管理器持有「当前一代」连接，mcp.reload 按 config.set 之后的新配置重连（运行中的 agent 仍要重启才换工具面）。
        var mcpManager = await McpManager.CreateAsync(() => configStore.Current).ConfigureAwait(false);
        builder.Services.AddSingleton(mcpManager);
        builder.Services.AddSingleton(mcpManager.Current);
        builder.Services.AddSingleton<IMcpControl>(mcpManager);
        builder.Services.AddSingleton<IToolCatalog>(static sp => new ToolCatalog(
            sp.GetRequiredService<ToolsetBuilder>(),
            sp.GetRequiredService<McpManager>()));

        builder.Services.AddShoyeeAgent();
        builder.Services.AddSingleton<IConfigReloader, ConfigReloader>();
        // 指令层要在 Agent 之后：/approve 依赖审批台，/stop 依赖轮次服务
        builder.Services.AddShoyeeHandlers();
        builder.Services.AddShoyeeProactive();
        // 登录项只有 daemon：菜单栏由它拉起，桌面端再由菜单栏按需启动
        builder.Services.AddHostedService<MenuLauncher>();

        // 渠道：管线先注册，适配器/发送器按配置决定装哪些。
        // 没有 IM 配置时只有桌面 Lane，ChannelHost 直接退出，不起接收循环。
        builder.Services.AddShoyeeChannels();
        builder.Services.AddShoyeeMattermostChannels(workspaceConfig.Channels);
        builder.Services.AddShoyeeWeChatChannel(workspaceConfig.Channels);

        var capabilities = new List<string>(RpcProtocol.Capabilities);
        if (workspaceConfig.Photo is not null)
        {
            capabilities.Add("photo");
            capabilities.Add("image");
        }

        if (workspaceConfig.Voice is { } voiceCapability
            && !IsDisabled(voiceCapability.ResolveSynthesisProvider()))
        {
            capabilities.Add("tts");
        }

        builder.Services.AddShoyeeGateway(new GatewayOptions
        {
            Version = RpcProtocol.Version,
            Workspace = workspaceConfig.Root,
            SocketPath = paths.SocketPath,
            AppDataRoot = paths.AppDataRoot,
            StartedAt = startedAt,
            Capabilities = capabilities,
            AllowTestRole = string.Equals(
                Environment.GetEnvironmentVariable("SHOYEE_ENV"),
                "test",
                StringComparison.OrdinalIgnoreCase),
            DatabaseInfo = () =>
            {
                var info = new FileInfo(dbPath);
                return new DbInfo(dbPath, info.Exists ? info.Length : 0);
            },
        });

        // 事件出口 = 桌面广播 + IM 回帖；必须在 AddShoyeeGateway() 之后覆盖（那里 Replace 成 EventHub）
        builder.Services.Replace(ServiceDescriptor.Singleton<ITurnEventSink>(sp => new CompositeTurnEventSink(
            sp.GetRequiredService<EventHub>(),
            sp.GetRequiredService<ChannelReplyRouter>(),
            sp.GetRequiredService<ChannelApprovalRouter>(),
            sp.GetRequiredService<ILogger<CompositeTurnEventSink>>())));

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shoyee.Daemon");
        foreach (var failure in mcpManager.Current.Failures)
        {
            logger.LogWarning("MCP 服务连接失败：{Failure}", failure);
        }

        try
        {
            await host.Services.GetRequiredService<IStorageInitializer>()
                .InitializeAsync()
                .ConfigureAwait(false);

            var bootstrap = WorkspaceBootstrap.EnsurePersonaFiles(workspaceConfig.Root);
            foreach (var message in bootstrap.Messages)
            {
                logger.LogInformation("{Message}", message);
            }

            if (workspaceConfig.Photo is not null)
            {
                var templates = PhotoWorkspaceTemplates.Ensure(workspaceConfig.Root);
                if (templates.Count > 0)
                {
                    logger.LogInformation(
                        "已补齐角色图词表/工作流模板：{Files}（可直接编辑，已有文件不会覆盖）",
                        string.Join("、", templates));
                }
            }

            var heartbeatTemplates = HeartbeatWorkspaceTemplates.Ensure(workspaceConfig.Root);
            if (heartbeatTemplates.Count > 0)
            {
                logger.LogInformation(
                    "已补齐心跳提示词：{Files}（可直接编辑，已有文件不会覆盖）",
                    string.Join("、", heartbeatTemplates));
            }

            var lanes = await LaneSeeder
                .EnsureLanesAsync(host.Services.GetRequiredService<ILaneRepository>(), workspaceConfig)
                .ConfigureAwait(false);
            logger.LogInformation(
                "已播种 {Count} 条 Lane：{Lanes}",
                lanes.Count,
                string.Join("、", lanes.Select(static lane => lane.Id)));
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "daemon 启动初始化失败");
            return DaemonExitCodes.StartupFailure;
        }

        var control = host.Services.GetRequiredService<DaemonControl>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        control.ShutdownRequested += lifetime.StopApplication;

        logger.LogInformation(
            "Shoyee daemon {Version} 启动：工作区 {Workspace}，socket {Socket}",
            RpcProtocol.Version,
            workspaceConfig.Root,
            paths.SocketPath);

        try
        {
            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "daemon 运行失败");
            return DaemonExitCodes.StartupFailure;
        }

        logger.LogInformation("Shoyee daemon 已退出");
        return DaemonExitCodes.Success;
    }

    /// <summary>配置里写 <c>none</c> = 关闭该方向。</summary>
    private static bool IsDisabled(string provider)
        => string.Equals(provider, "none", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// launchd 下落盘到 <c>logs/daemon.log</c>：Console 输出已被重定向时接管，
    /// 交互式终端保持原样（不打断开发时的实时输出）。
    /// </summary>
    private static void RedirectConsoleToLogFile(ShoyeePaths paths)
    {
        if (!Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            paths.EnsureLogDirectory();
            var stream = new FileStream(paths.LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var writer = new StreamWriter(stream) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 日志写不了不该拦启动：launchd 自己的 stdout 仍然有输出
        }
    }
}
