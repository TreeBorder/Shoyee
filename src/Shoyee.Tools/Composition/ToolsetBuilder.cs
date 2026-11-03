using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Notes;
using Shoyee.Tools.Approval;
using Shoyee.Tools.Cron;
using Shoyee.Tools.Files;
using Shoyee.Tools.Media;
using Shoyee.Tools.Notes;
using Shoyee.Tools.Photo;
using Shoyee.Tools.Process;
using Shoyee.Tools.Memory;
using Shoyee.Tools.State;
using Shoyee.Tools.Shell;
using Shoyee.Tools.Voice;
using Shoyee.Tools.Web;

namespace Shoyee.Tools.Composition;

/// <summary>
/// profile → 工具面：按元数据过滤 scope / 禁用名单，再按需挂审批包装。
///
/// **Work** 走 HarnessAgent，有审批中间件：写文件 / terminal / process_start 包装成
/// <see cref="ApprovalRequiredAIFunction"/>，白名单与安全命令由
/// <see cref="ToolApprovalRules"/> 自动放行。
/// **Roleplay** 走普通 ChatClientAgent，没有审批中间件 → 只挂只读工具（越权能力不上桌）。
/// MCP 工具不包装（与 Secretary 一致，由服务端自己把关）。
/// </summary>
public sealed class ToolsetBuilder
{
    private readonly WorkspaceConfig _config;
    private readonly ToolsetServices _services;
    private readonly ToolPathPolicy _pathPolicy;

    /// <summary>创建组装器。</summary>
    /// <param name="config">工作区配置。</param>
    /// <param name="services">可选工具依赖。</param>
    public ToolsetBuilder(WorkspaceConfig config, ToolsetServices? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _services = services ?? new ToolsetServices();
        _pathPolicy = ToolPathPolicy.FromConfig(config);
    }

    /// <summary>
    /// 热更新 <c>tools.deniedPaths</c>：只换共享的路径黑名单，不重建工具、不碰 agent/请求前缀。
    /// 其他工具配置（禁用名单、MCP、子智能体）改了仍需重启。
    /// </summary>
    /// <param name="config">新的工作区配置（只读其中的 <c>deniedPaths</c>）。</param>
    public void ReloadPathPolicy(WorkspaceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _pathPolicy.Reload(config);
    }

    /// <summary>构建指定档位的工具面。</summary>
    /// <param name="profile">工作 / RP。</param>
    /// <param name="requireApproval">
    /// 是否把需要审批的工具包成 <see cref="ApprovalRequiredAIFunction"/>。工作 agent 用默认 true；
    /// 子智能体用 false——子任务的审批不透传父 agent，包了只会永远挂起。
    /// </param>
    public Toolset Build(ToolProfile profile, bool requireApproval = true)    {
        var disabled = new HashSet<string>(_config.Tools.Disabled ?? [], StringComparer.Ordinal);
        var descriptors = CollectDescriptors(profile);

        var tools = new List<AITool>();
        foreach (var descriptor in descriptors)
        {
            if (disabled.Contains(descriptor.Metadata.Name) || !IsVisible(descriptor.Metadata.Scope, profile))
            {
                continue;
            }

            if (requireApproval
                && profile == ToolProfile.Work
                && descriptor.Metadata.RequiresApproval
                && descriptor.Tool is AIFunction function)
            {
                tools.Add(new ApprovalRequiredAIFunction(function));
            }
            else
            {
                tools.Add(descriptor.Tool);
            }
        }

        // MCP 工具的能力无法从元数据判断（可能带写）；RP 没有审批中间件，只给工作模式。
        if (profile == ToolProfile.Work && _services.Mcp is { Tools.Count: > 0 } mcp)
        {
            tools.AddRange(mcp.Tools.Where(tool => !IsMcpDisabled(tool, disabled)));
        }

        var rules = profile == ToolProfile.Work
            ? new List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>>
            {
                ToolApprovalRules.WorkspacePathsAutoApprovalRule(_config, (_services.Notes as ILocalNotesFiles)?.Root),
                ToolApprovalRules.SafeShellAutoApprovalRule,
            }
            : [];

        return new Toolset(tools, rules);
    }

    /// <summary>两类 profile 用到的全部工具描述（并集，按名字去重；设置页 <c>tools.list</c> 用）。</summary>
    public IReadOnlyList<ToolDescriptor> DescribeAll()
    {
        var byName = new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal);
        foreach (var profile in new[] { ToolProfile.Work, ToolProfile.Roleplay })
        {
            foreach (var descriptor in CollectDescriptors(profile))
            {
                byName.TryAdd(descriptor.Metadata.Name, descriptor);
            }
        }

        return byName.Values.ToList();
    }

    /// <summary>当前显式禁用的工具名（<c>tools.disabled</c>）。</summary>
    public IReadOnlySet<string> DisabledTools
        => new HashSet<string>(_config.Tools.Disabled ?? [], StringComparer.Ordinal);

    private List<ToolDescriptor> CollectDescriptors(ToolProfile profile)
    {
        // 文件工具两种模式共用；RP 没有审批中间件，写路径直接围在工作区 + 笔记库内。
        var descriptors = new List<ToolDescriptor>(
            FileAccessTools.Create(
                _config,
                confineWrites: profile == ToolProfile.Roleplay,
                notesRoot: (_services.Notes as ILocalNotesFiles)?.Root,
                pathPolicy: _pathPolicy));

        // 出站附件：把工作区/笔记库里的现有文件随回复发出去（两种模式同一道围栏）
        descriptors.Add(SendFileTool.Create(_config, (_services.Notes as ILocalNotesFiles)?.Root, _pathPolicy));

        if (_services.SessionSearch is not null)
        {
            descriptors.AddRange(HistoryTool.Create(_services.SessionSearch));
        }

        if (_services.Index is not null)
        {
            descriptors.AddRange(IndexTool.Create(_services.Index));
        }

        if (_services.Web is not null)
        {
            descriptors.AddRange(WebTool.Create(_services.Web));
        }

        // 语音：哪个方向配了 provider 就挂哪些工具（voice_speak/help 靠合成，voice_transcribe 靠转写）
        descriptors.AddRange(VoiceTools.Create(_config, _services.Synthesizer, _services.Transcriber));

        // 图片：工作模式 image_generate；RP 的 camera/gallery/random_outfit/support 靠词表 + 队列
        if (_services.Photo is not null || _services.PhotoVocabulary is not null)
        {
            descriptors.AddRange(PhotoTools.Create(
                _services.Photo,
                _services.PhotoVocabulary,
                _services.PhotoTasks,
                _services.States));
        }

        if (_services.Notes is { Configured: true } notes)
        {
            descriptors.AddRange(NotesTools.Create(notes));
        }

        if (ImageAnalysisTool.Create(_config, _config.Models.Vision, pathPolicy: _pathPolicy) is { } image)
        {
            descriptors.Add(image);
        }

        if (_config.Proactive.Cron && _services.Cron is not null && _services.CronScheduler is not null)
        {
            var mode = profile == ToolProfile.Roleplay ? Shoyee.Contracts.Domain.Mode.Roleplay : Shoyee.Contracts.Domain.Mode.Work;
            descriptors.AddRange(CronTool.Create(
                _services.Cron,
                _services.CronScheduler,
                _services.Messages,
                mode,
                _config.Proactive.EffectiveCron.MaxJobs));
        }

        if (profile == ToolProfile.Work)
        {
            descriptors.Add(ToolDescriptor.Create(ShellTool.Create(_config), ToolScope.Work, writes: true, requiresApproval: true));
            if (_services.Processes is not null)
            {
                descriptors.AddRange(ProcessTool.Create(_services.Processes));
            }
        }
        else if (_services.States is not null)
        {
            descriptors.AddRange(StateTool.Create(_services.States, _services.Events));
        }

        return descriptors;
    }

    private static bool IsVisible(ToolScope scope, ToolProfile profile)
        => scope == ToolScope.All
           || (scope == ToolScope.Work && profile == ToolProfile.Work)
           || (scope == ToolScope.Roleplay && profile == ToolProfile.Roleplay);

    private static bool IsMcpDisabled(AITool tool, HashSet<string> disabled)
        => tool is AIFunction function && function.Name is { } name && disabled.Contains(name);
}
