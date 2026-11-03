using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Gateway.Rpc;

/// <summary>方法处理器。</summary>
internal delegate Task<object?> RpcHandler(RpcCall call, CancellationToken cancellationToken);

/// <summary>
/// 方法注册表：把 <see cref="RpcMethods"/> 常量映射到阶段 1/2 的领域服务。
/// 只做参数校验与角色约束，业务语义（错误码）由服务层抛 <see cref="RpcException"/>。
/// </summary>
internal static class RpcHandlers
{
    /// <summary>本版本已实现的方法；未注册的一律 <c>method_not_found</c>。</summary>
    public static Dictionary<string, RpcHandler> Build(
        RpcServices services,
        ShutdownGate gate,
        DaemonControl control)
    {
        var handlers = new Dictionary<string, RpcHandler>(StringComparer.Ordinal)
        {
            [RpcMethods.Hello] = async (call, _) => await HelloAsync(services, call).ConfigureAwait(false),
            [RpcMethods.Ping] = static (_, _) => Task.FromResult<object?>(new { }),

            [RpcMethods.SessionList] = async (call, ct) =>
                await services.Sessions.ListAsync(call.Params<SessionListRequest>(), ct).ConfigureAwait(false),
            [RpcMethods.SessionGet] = async (call, ct) =>
                await services.Sessions.GetAsync(call.RequireString("sessionId"), ct).ConfigureAwait(false),
            [RpcMethods.SessionOpen] = async (call, ct) =>
                await services.Sessions.OpenAsync(call.RequireString("sessionId"), ct).ConfigureAwait(false),
            [RpcMethods.SessionCreate] = async (call, ct) =>
            {
                RejectWhenStopping(gate);
                return await services.Sessions.CreateAsync(call.Params<SessionCreateRequest>(), ct)
                    .ConfigureAwait(false);
            },
            [RpcMethods.SessionRename] = async (call, ct) =>
            {
                var request = call.Params<SessionRenameRequest>();
                await services.Sessions.RenameAsync(request.SessionId, request.Title, ct).ConfigureAwait(false);
                return new { };
            },
            [RpcMethods.SessionArchive] = async (call, ct) =>
            {
                await services.Sessions.ArchiveAsync(call.RequireString("sessionId"), ct).ConfigureAwait(false);
                return new { };
            },
            [RpcMethods.SessionSetActiveForLane] = async (call, ct) =>
            {
                var request = call.Params<SessionActiveForLaneRequest>();
                await services.Sessions.SetActiveForLaneAsync(request.SessionId, request.LaneId, ct)
                    .ConfigureAwait(false);
                return new { };
            },
            [RpcMethods.SessionMessages] = async (call, ct) =>
            {
                var request = call.Params<SessionMessagesRequest>();
                var result = await services.Sessions.GetMessagesAsync(
                        request.SessionId, request.BeforeId, request.AfterId, request.Limit, ct)
                    .ConfigureAwait(false);

                var lastCall = await services.Usage
                    .GetLastCallAsync(request.SessionId, ct)
                    .ConfigureAwait(false);

                return lastCall is null
                    ? result
                    : result with
                    {
                        Context = new ContextUsage(
                            lastCall.InputTokens,
                            lastCall.OutputTokens,
                            lastCall.CacheHitTokens,
                            lastCall.CreatedAt),
                    };
            },
            [RpcMethods.LaneMessages] = async (call, ct) =>
            {
                var request = call.Params<LaneMessagesRequest>();
                // 桌面 RP 可以切换查看任意 RP Lane 的历史；不传就默认自己的 Lane。
                var laneId = await ResolveReadableLaneAsync(call, request.LaneId, services.Lanes, ct)
                    .ConfigureAwait(false);
                return await services.Sessions.GetLaneMessagesAsync(
                        laneId, request.BeforeId, request.AfterId, request.Limit, ct)
                    .ConfigureAwait(false);
            },
            [RpcMethods.SessionSearch] = async (call, ct) =>
            {
                var request = call.Params<SessionSearchRequest>();
                if (string.IsNullOrWhiteSpace(request.Query))
                {
                    throw new RpcException(RpcErrors.InvalidParams, "query 不能为空");
                }

                // RP 桌面可以跨 Lane 检索 RP 历史（范围锁在 roleplay，不碰工作会话）
                var roleplay = call.Connection.Role == ClientRole.DesktopRoleplay;
                var hits = await services.Search.SearchSessionsAsync(
                        request.Query,
                        new SessionSearchFilter(
                            roleplay ? Mode.Roleplay : request.Mode,
                            request.LaneId,
                            request.Limit),
                        ct)
                    .ConfigureAwait(false);
                return new SessionSearchResult(hits);
            },
            [RpcMethods.SessionSnapshot] = async (call, ct) =>
                await services.Sessions
                    .GetSnapshotAsync(call.RequireString("sessionId"), ct)
                    .ConfigureAwait(false),
            [RpcMethods.SessionExport] = async (call, ct) =>
                await services.Sessions
                    .ExportAsync(call.Params<SessionExportRequest>(), ct)
                    .ConfigureAwait(false),

            [RpcMethods.TurnSend] = async (call, ct) =>
            {
                RejectWhenStopping(gate);
                var request = call.Params<TurnSendRequest>();
                if (call.Connection.Role == ClientRole.DesktopRoleplay)
                {
                    if (!string.IsNullOrWhiteSpace(request.SessionId))
                    {
                        throw new RpcException(RpcErrors.UnauthorizedRole, "该连接不能操作工作会话");
                    }

                    request = request with { LaneId = ResolveLane(call, request.LaneId), SessionId = null };
                }

                return await services.Turns.SendAsync(request, ct).ConfigureAwait(false);
            },
            [RpcMethods.TurnCancel] = async (call, ct) =>
            {
                var request = call.Params<TurnCancelRequest>();
                var sessionId = call.Connection.Role == ClientRole.DesktopRoleplay
                    ? ResolveLane(call, request.SessionId)
                    : request.SessionId;
                await services.Turns.CancelAsync(sessionId, request.TurnId, ct).ConfigureAwait(false);
                return new { };
            },
            [RpcMethods.TurnRetry] = async (call, ct) =>
            {
                RejectWhenStopping(gate);
                if (call.Connection.Role == ClientRole.DesktopRoleplay)
                {
                    throw new RpcException(RpcErrors.InvalidParams, "RP 时间线不支持重试");
                }

                return await services.Turns
                    .RetryAsync(call.Params<TurnRetryRequest>().SessionId, ct)
                    .ConfigureAwait(false);
            },
            [RpcMethods.ApprovalRespond] = async (call, ct) =>
            {
                await services.Turns.RespondApprovalAsync(call.Params<ApprovalRespondRequest>(), ct)
                    .ConfigureAwait(false);
                return new { };
            },

            [RpcMethods.TtsSynthesize] = async (call, ct) =>
            {
                if (services.Tts is null)
                {
                    throw new RpcException(
                        RpcErrors.Internal,
                        "语音合成未配置（voice.synthesis = none 或缺少 provider）");
                }

                return await services.Tts
                    .SynthesizeAsync(call.Params<TtsSynthesizeRequest>(), ct)
                    .ConfigureAwait(false);
            },
            [RpcMethods.ImageRender] = async (call, ct) =>
            {
                if (services.Images is null)
                {
                    throw new RpcException(
                        RpcErrors.Internal,
                        "出图未配置（photo.provider 缺失或未接入供应商）");
                }

                return await services.Images
                    .RenderAsync(call.Params<ImageRenderRequest>(), ct)
                    .ConfigureAwait(false);
            },
            [RpcMethods.AssetGet] = (call, _) =>            {
                if (services.Assets is null)
                {
                    throw new RpcException(RpcErrors.Internal, "资产登记未接入");
                }

                var assetId = call.RequireString("assetId");
                var asset = services.Assets.Get(assetId)
                            ?? throw new RpcException(
                                RpcErrors.NotFound,
                                "资产不存在或不在 assets/ 下",
                                new { assetId });
                return Task.FromResult<object?>(asset);
            },

            [RpcMethods.StatusGet] = async (call, ct) => await StatusAsync(services, ct).ConfigureAwait(false),
            [RpcMethods.DaemonStatus] = async (call, ct) => await DaemonStatusAsync(services, ct).ConfigureAwait(false),

            [RpcMethods.ToolsList] = (call, _) =>
            {
                if (services.ToolCatalog is null)
                {
                    throw new RpcException(RpcErrors.Internal, "工具清单未接入");
                }

                var request = call.Parameters is null ? new ToolListRequest() : call.Params<ToolListRequest>();
                var items = services.ToolCatalog.List();
                if (!string.IsNullOrWhiteSpace(request.Scope)
                    && !string.Equals(request.Scope, "all", StringComparison.OrdinalIgnoreCase))
                {
                    var scope = request.Scope.Trim();
                    items = items
                        .Where(item => string.Equals(item.Scope, scope, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(item.Scope, "all", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (!string.IsNullOrWhiteSpace(request.Query))
                {
                    var query = request.Query.Trim();
                    items = items
                        .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                       || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                return Task.FromResult<object?>(new ToolListResult(items));
            },
            [RpcMethods.McpStatus] = (_, _) =>
            {
                if (services.Mcp is null)
                {
                    throw new RpcException(RpcErrors.Internal, "MCP 连接管理未接入");
                }

                return Task.FromResult<object?>(new McpStatusResult(services.Mcp.Status));
            },
            [RpcMethods.McpReload] = async (call, ct) =>
            {
                RejectWhenStopping(gate);
                if (services.Mcp is null)
                {
                    throw new RpcException(RpcErrors.Internal, "MCP 连接管理未接入");
                }

                return await services.Mcp.ReloadAsync(ct).ConfigureAwait(false);
            },
            [RpcMethods.StateSet] = async (call, ct) =>
            {
                var request = call.Params<StateSetRequest>();
                if (string.IsNullOrWhiteSpace(request.Key))
                {
                    throw new RpcException(RpcErrors.InvalidParams, "key 不能为空");
                }

                var key = request.Key.Trim();
                var changed = await services.States
                    .SetAsync(key, request.Value ?? string.Empty, byAgent: false, ct)
                    .ConfigureAwait(false);
                if (!changed)
                {
                    throw new RpcException(RpcErrors.NotFound, "状态不存在", new { key });
                }

                var record = await services.States.GetAsync(key, ct).ConfigureAwait(false)
                             ?? throw new RpcException(RpcErrors.NotFound, "状态不存在", new { key });
                var dto = ToStateDto(record);
                if (services.Events is not null)
                {
                    await services.Events.PublishAsync(RpcEvents.StateUpdated, dto, ct).ConfigureAwait(false);
                }

                return dto;
            },
            [RpcMethods.UsageGet] = async (call, ct) =>
            {
                var request = call.Parameters is null ? new UsageGetRequest() : call.Params<UsageGetRequest>();
                var now = DateTimeOffset.UtcNow;
                var from = request.From ?? new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
                var to = request.To ?? now;
                if (to < from)
                {
                    throw new RpcException(RpcErrors.InvalidParams, "to 不能早于 from");
                }

                var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim();
                var totals = await services.Usage
                    .GetTotalsAsync(from, to, sessionId, ct)
                    .ConfigureAwait(false);
                return new UsageGetResult(
                    new UsageSummary(
                        totals.Calls,
                        totals.InputTokens,
                        totals.OutputTokens,
                        totals.CacheHitTokens,
                        totals.CacheMissTokens),
                    from,
                    to);
            },

            [RpcMethods.ConfigGet] = (_, _) =>
                Task.FromResult<object?>(services.ConfigReloader?.Current ?? services.Config),
            [RpcMethods.ConfigSet] = async (call, ct) =>
            {
                RejectWhenStopping(gate);
                if (services.ConfigReloader is null)
                {
                    throw new RpcException(RpcErrors.Internal, "配置写入未接入");
                }

                return await services.ConfigReloader.SetAsync(call.Params<ConfigSetRequest>(), ct)
                    .ConfigureAwait(false);
            },
            [RpcMethods.WorkspacePaths] = (_, _) =>
                Task.FromResult<object?>(BuildWorkspacePaths(services)),
            [RpcMethods.ConfigReload] = async (call, ct) =>
            {
                RejectWhenStopping(gate);
                if (services.ConfigReloader is null)
                {
                    throw new RpcException(RpcErrors.Internal, "配置重载未接入");
                }

                return await services.ConfigReloader.ReloadAsync(ct).ConfigureAwait(false);
            },

            // 通知自检：配置完渠道后从桌面/菜单栏触发一次，失败原因原样返回（不折叠成 internal）
            [RpcMethods.NotifyTest] = async (call, ct) =>
            {
                if (services.Notifications is null)
                {
                    throw new RpcException(RpcErrors.Internal, "通知渠道未配置（notify.provider = none）");
                }

                var request = call.Parameters is null ? new NotifyTestRequest() : call.Params<NotifyTestRequest>();
                var provider = ((services.ConfigReloader?.Current ?? services.Config).Notify?.Provider ?? "system").Trim();
                var title = string.IsNullOrWhiteSpace(request.Title) ? "Shoyee 通知测试" : request.Title;
                var body = string.IsNullOrWhiteSpace(request.Body)
                    ? $"这是一条测试通知（{DateTimeOffset.Now:HH:mm:ss}）。"
                    : request.Body;
                try
                {
                    await services.Notifications.PushAsync(title, body, ct).ConfigureAwait(false);
                    return new NotifyTestResult(provider, true, null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new NotifyTestResult(provider, false, ex.Message);
                }
            },

            [RpcMethods.IndexList] = async (call, ct) =>
            {
                var request = call.Parameters is null ? new IndexListRequest() : call.Params<IndexListRequest>();
                var items = await services.Index.ListAsync(request.Scope, request.Query, cancellationToken: ct).ConfigureAwait(false);
                return new IndexListResult(items);
            },
            [RpcMethods.IndexUpsert] = async (call, ct) =>
            {
                var request = call.Params<IndexUpsertRequest>();
                if (string.IsNullOrWhiteSpace(request.Entry.Key))
                {
                    throw new RpcException(RpcErrors.InvalidParams, "entry.key 不能为空");
                }

                return await services.Index.UpsertAsync(request.Entry, ct).ConfigureAwait(false);
            },
            [RpcMethods.IndexRemove] = async (call, ct) =>
            {
                await services.Index.RemoveAsync(call.RequireString("key"), ct).ConfigureAwait(false);
                return new { };
            },

            // 先回响应再触发停机：直接触发会让 host 在响应写出前开始关连接
            [RpcMethods.DaemonShutdown] = (_, _) =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    control.RequestShutdown();
                });
                return Task.FromResult<object?>(new { });
            },

            // 进程内没有「重启自己」：退出后由 launchd（KeepAlive）拉起，语义与 shutdown 相同
            [RpcMethods.DaemonRestart] = (_, _) =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    control.RequestShutdown();
                });
                return Task.FromResult<object?>(new { restarting = true });
            },
        };

        if (services.Cron is not null)
        {
            AddCronHandlers(handlers, services, services.Cron);
        }

        return handlers;
    }

    /// <summary>
    /// 定时任务：任务页只读 + 启停/删除。任务创建仍由模型用 cron 工具完成（工作模式走审批），
    /// 这里不提供「手填调度串」的入口，免得两处校验规则分叉。
    /// 只暴露工作模式任务：RP 任务是角色的时间线，管理入口在 RP 对话里的工具。
    /// </summary>
    private static void AddCronHandlers(
        Dictionary<string, RpcHandler> handlers,
        RpcServices services,
        ICronRepository jobs)
    {
        handlers[RpcMethods.CronList] = async (call, ct) =>
        {
            var request = call.Parameters is null ? new CronListRequest() : call.Params<CronListRequest>();
            var rows = await jobs.ListAsync(enabledOnly: !request.IncludeDisabled, ct).ConfigureAwait(false);
            var items = rows
                .Where(static job => job.Mode == Mode.Work)
                .Select(ToCronDto)
                .OrderBy(static job => job.NextRunAt ?? DateTimeOffset.MaxValue)
                .ThenBy(static job => job.Name, StringComparer.Ordinal)
                .ToList();
            return new CronListResult(items);
        };

        handlers[RpcMethods.CronGet] = async (call, ct) =>
        {
            var request = call.Params<CronGetRequest>();
            var job = await RequireWorkJobAsync(jobs, request.JobId, ct).ConfigureAwait(false);
            var runs = await jobs
                .ListExecutionsAsync(job.Id, Math.Clamp(request.Executions, 1, 50), ct)
                .ConfigureAwait(false);
            return new CronJobDetail(ToCronDto(job), runs.Select(ToCronExecutionDto).ToList());
        };

        handlers[RpcMethods.CronExecutions] = async (call, ct) =>
        {
            var request = call.Params<CronExecutionsRequest>();
            var job = await RequireWorkJobAsync(jobs, request.JobId, ct).ConfigureAwait(false);
            var runs = await jobs
                .ListExecutionsAsync(job.Id, Math.Clamp(request.Limit, 1, 100), ct)
                .ConfigureAwait(false);
            return new CronExecutionsResult(runs.Select(ToCronExecutionDto).ToList());
        };

        handlers[RpcMethods.CronSetEnabled] = async (call, ct) =>
        {
            var request = call.Params<CronSetEnabledRequest>();
            var job = await RequireWorkJobAsync(jobs, request.JobId, ct).ConfigureAwait(false);
            var changed = await jobs.SetEnabledAsync(job.Id, request.Enabled, ct).ConfigureAwait(false);
            if (!changed)
            {
                throw new RpcException(RpcErrors.NotFound, "task not found", new { jobId = job.Id });
            }

            await RefreshSchedulerAsync(services, job.Id, ct).ConfigureAwait(false);
            return new { };
        };

        handlers[RpcMethods.CronRemove] = async (call, ct) =>
        {
            var request = call.Params<CronRemoveRequest>();
            var job = await RequireWorkJobAsync(jobs, request.JobId, ct).ConfigureAwait(false);
            await jobs.DeleteAsync(job.Id, ct).ConfigureAwait(false);
            await RefreshSchedulerAsync(services, job.Id, ct).ConfigureAwait(false);
            return new { };
        };
    }

    private static async Task<CronJobRecord> RequireWorkJobAsync(
        ICronRepository jobs,
        string jobId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new RpcException(RpcErrors.InvalidParams, "jobId 不能为空");
        }

        var job = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        // RP 任务对工作桌面按「不存在」处理，不泄露角色的时间线安排
        if (job is null || job.Mode != Mode.Work)
        {
            throw new RpcException(RpcErrors.NotFound, "task not found", new { jobId });
        }

        return job;
    }

    private static async Task RefreshSchedulerAsync(
        RpcServices services,
        string jobId,
        CancellationToken cancellationToken)
    {
        if (services.CronScheduler is null)
        {
            throw new RpcException(RpcErrors.Internal, "定时任务调度器未启动");
        }

        await services.CronScheduler.RefreshAsync(jobId, cancellationToken).ConfigureAwait(false);
    }

    private static CronJobDto ToCronDto(CronJobRecord job) => new(
        job.Id,
        job.Name,
        job.Schedule,
        job.Kind,
        job.Payload,
        string.IsNullOrWhiteSpace(job.Deliver) ? "local" : job.Deliver,
        job.Enabled,
        job.Mode,
        job.LaneId,
        job.SessionId,
        job.NextRunAt,
        job.LastRunAt,
        job.LastStatus,
        job.LastError,
        job.FailureStreak,
        job.CreatedAt);

    private static CronExecutionDto ToCronExecutionDto(CronExecutionRecord run) => new(
        run.JobId,
        run.ScheduledFor,
        run.StartedAt,
        run.FinishedAt,
        run.State,
        run.Result);

    private static async Task<HelloResult> HelloAsync(RpcServices services, RpcCall call)
    {
        var request = call.Params<HelloRequest>();
        if (call.Connection.HelloCompleted)
        {
            throw new RpcException(RpcErrors.InvalidParams, "已经握手，不能重复 rpc.hello");
        }

        if (request.Role == ClientRole.Test && !services.Options.AllowTestRole)
        {
            throw new RpcException(RpcErrors.UnauthorizedRole, "test 角色只在测试环境可用");
        }

        var serverMajor = RpcProtocol.MajorOf(services.Options.Version);
        var clientMajor = RpcProtocol.MajorOf(request.Version);
        if (clientMajor is null)
        {
            throw new RpcException(RpcErrors.InvalidParams, "version 必须是 major.minor.patch");
        }

        if (clientMajor != serverMajor)
        {
            throw new RpcException(
                RpcErrors.IncompatibleVersion,
                "协议主版本不一致",
                new { server = services.Options.Version, client = request.Version });
        }

        if (request.Role == ClientRole.DesktopRoleplay && string.IsNullOrWhiteSpace(request.LaneId))
        {
            throw new RpcException(RpcErrors.InvalidParams, "desktop-roleplay 必须给 laneId");
        }

        var lanes = await services.Lanes.ListAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        var helloLanes = lanes
            .Select(lane => new HelloLane(
                lane.Id,
                lane.Channel,
                lane.Mode,
                IsConnected(lane, services.Channels?.FirstOrDefault(a => a.LaneId == lane.Id))))
            .ToList();

        // 全部校验与查询成功后才落状态：失败时连接仍是「未握手」，不会拿到任何权限
        call.Connection.Client = string.IsNullOrWhiteSpace(request.Client) ? "unknown" : request.Client;
        call.Connection.ClientVersion = request.Version;
        call.Connection.Role = request.Role;
        call.Connection.LaneId = request.Role == ClientRole.DesktopRoleplay
            ? request.LaneId!.Trim()
            : string.IsNullOrWhiteSpace(request.LaneId) ? null : request.LaneId.Trim();
        call.Connection.HelloCompleted = true;
        return new HelloResult(
            services.Options.Version,
            services.Options.Workspace,
            services.Options.StartedAt,
            services.Options.Capabilities,
            helloLanes);
    }

    private static async Task<StatusDto> StatusAsync(RpcServices services, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lanes = await BuildLaneStatusesAsync(services.Lanes, cancellationToken, services.Channels).ConfigureAwait(false);
        var states = await services.States.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var totals = await services.Usage.GetTotalsAsync(dayStart, now, cancellationToken: cancellationToken).ConfigureAwait(false);
        var db = services.Options.DatabaseInfo?.Invoke() ?? new DbInfo(string.Empty, 0);
        return new StatusDto(
            services.Options.Version,
            services.Options.StartedAt,
            (long)Math.Max(0, (now - services.Options.StartedAt).TotalSeconds),
            services.Options.Workspace,
            lanes,
            new UsageSummary(
                totals.Calls,
                totals.InputTokens,
                totals.OutputTokens,
                totals.CacheHitTokens,
                totals.CacheMissTokens),
            states.Select(ToStateDto).ToList(),
            db,
            LastError: null);
    }

    private static StateDto ToStateDto(StateRecord state) => new(
        state.Key,
        state.Name,
        state.Value,
        state.Description,
        state.AgentEditable,
        state.UpdatedAt);

    private static async Task<DaemonStatusDto> DaemonStatusAsync(RpcServices services, CancellationToken cancellationToken)
    {
        var lanes = await BuildLaneStatusesAsync(services.Lanes, cancellationToken, services.Channels).ConfigureAwait(false);
        return new DaemonStatusDto(
            services.Options.Version,
            (long)Math.Max(0, (DateTimeOffset.UtcNow - services.Options.StartedAt).TotalSeconds),
            services.Options.SocketPath,
            lanes);
    }

    /// <summary>
    /// 桌面要读的本地文件路径。只由 daemon 拼固定位置，客户端不能传任意路径；
    /// 文件本身仍同机直读，不经过 RPC 帧。
    /// </summary>
    /// <param name="services">RPC 依赖（工作区与 app data 根）。</param>
    internal static WorkspacePathsDto BuildWorkspacePaths(RpcServices services)
    {
        var root = services.Options.Workspace;
        var appData = string.IsNullOrWhiteSpace(services.Options.AppDataRoot)
            ? Shoyee.Client.Transport.ShoyeePaths.Default.AppDataRoot
            : services.Options.AppDataRoot;

        return new WorkspacePathsDto(
            root,
            Path.Combine(root, "config.json"),
            new PromptPathsDto(
                Path.Combine(root, "AGENTS.shared.md"),
                Path.Combine(root, "SOUL.md"),
                Path.Combine(root, "USER.md"),
                Path.Combine(root, "MEMORY.md"),
                Path.Combine(root, "modes", "roleplay", "AGENTS.md"),
                Path.Combine(root, "modes", "roleplay", "WORLD.md"),
                Path.Combine(root, "modes", "work", "AGENTS.md")),
            new HeartbeatPathsDto(
                Path.Combine(root, ".heartbeat", "prompts.json"),
                Path.Combine(root, ".heartbeat", "heartbeat.log")),
            Path.Combine(appData, "logs", "daemon.log"),
            Path.Combine(root, "exports"),
            Path.Combine(appData, "avatars"));
    }

    /// <summary>Lane 状态：桌面恒为在线；IM 的在线状态来自适配器。</summary>
    internal static async Task<IReadOnlyList<LaneStatus>> BuildLaneStatusesAsync(
        ILaneRepository lanes,
        CancellationToken cancellationToken,
        IReadOnlyList<IChannelAdapter>? adapters = null)
    {
        var rows = await lanes.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return rows.Select(lane =>
            {
                var adapter = adapters?.FirstOrDefault(a => a.LaneId == lane.Id);
                return new LaneStatus(
                    lane.Id,
                    lane.Channel,
                    lane.Mode,
                    lane.DisplayName,
                    IsConnected(lane, adapter),
                    lane.ActiveSessionId,
                    adapter?.LastError);
            })
            .ToList();
    }

    private static bool IsConnected(LaneRecord lane, IChannelAdapter? adapter)
        => lane.Channel == LaneChannel.Desktop || adapter?.State == ChannelConnectionState.Connected;

    /// <summary>
    /// 桌面 RP 连接只能<b>写</b>自己的 Lane（发消息、取消、审批）：请求没给就补上，给了别的就拒绝。
    /// 读历史走 <see cref="ResolveReadableLaneAsync"/>。
    /// </summary>
    private static string ResolveLane(RpcCall call, string? requested)
    {
        if (call.Connection.Role != ClientRole.DesktopRoleplay)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                throw new RpcException(RpcErrors.InvalidParams, "laneId 不能为空");
            }

            return requested;
        }

        var own = call.Connection.LaneId;
        if (string.IsNullOrWhiteSpace(own))
        {
            throw new RpcException(RpcErrors.UnauthorizedRole, "该连接没有绑定 Lane");
        }

        if (!string.IsNullOrWhiteSpace(requested)
            && !string.Equals(requested, own, StringComparison.Ordinal))
        {
            throw new RpcException(RpcErrors.UnauthorizedRole, "该连接只能操作自己的 Lane");
        }

        return own;
    }

    /// <summary>
    /// 读历史的 Lane 解析：桌面 RP 可以读任意 <b>RP</b> Lane（工作 Lane 一律拒绝），
    /// 不传就默认自己的 Lane；其他角色照旧必须显式给。
    /// </summary>
    private static async Task<string> ResolveReadableLaneAsync(
        RpcCall call,
        string? requested,
        ILaneRepository lanes,
        CancellationToken cancellationToken)
    {
        if (call.Connection.Role != ClientRole.DesktopRoleplay
            || string.IsNullOrWhiteSpace(requested))
        {
            return ResolveLane(call, requested);
        }

        var lane = await lanes.GetAsync(requested, cancellationToken).ConfigureAwait(false);
        if (lane is null || lane.Mode != Mode.Roleplay)
        {
            throw new RpcException(RpcErrors.UnauthorizedRole, "该连接只能读取 RP Lane");
        }

        return requested;
    }

    private static void RejectWhenStopping(ShutdownGate gate)
    {
        if (gate.IsStopping)
        {
            throw new RpcException(RpcErrors.Busy, "daemon 正在退出，不再接受新轮次");
        }
    }
}
