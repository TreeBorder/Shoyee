using System.Text.Json;
using System.Text.RegularExpressions;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Daemon.Configuration;

/// <summary>
/// <c>config.set</c> 的字段校验：**拦明显改坏的配置**（缺字段、越界、把明文密钥写进文件），
/// 不替实现方校验供应商自己的配置节——那部分由各 provider 反序列化时负责。
///
/// 硬错误抛 <c>invalid_params</c>（文件不落盘）；「能用但现在不生效」的情况收进 warnings，
/// 例如引用的环境变量没设置、子智能体开了免审批终端、上下文窗口小到压缩不会被触发。
/// </summary>
public static class ConfigValidator
{
    private static readonly Regex EnvironmentName = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>校验整份配置。</summary>
    /// <param name="config">待写入的配置。</param>
    /// <returns>不拦保存的提醒。</returns>
    /// <exception cref="RpcException">校验失败（<c>invalid_params</c>）。</exception>
    public static IReadOnlyList<string> Validate(WorkspaceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var warnings = new List<string>();

        ValidateModels(config.Models, warnings);
        ValidateChannels(config.Channels, warnings);
        ValidateProactive(config.Proactive);
        ValidateTools(config.Tools, warnings);
        ValidateShell(config.Shell);
        ValidateAttachments(config.Attachments);
        ValidateContext(config.Context, warnings);
        ValidateDatabase(config);
        ValidateProviders(config, warnings);

        return warnings;
    }

    private static void ValidateModels(ModelProfiles models, List<string> warnings)
    {
        ValidateEndpoint(models.Main, "models.main", warnings, required: true);
        ValidateEndpoint(models.Small, "models.small", warnings, required: true);
        ValidateEndpoint(models.Vision, "models.vision", warnings, required: false);
    }

    private static void ValidateEndpoint(
        ModelEndpoint? endpoint,
        string section,
        List<string> warnings,
        bool required)
    {
        if (endpoint is null)
        {
            if (required)
            {
                Fail($"{section} 不能为空");
            }

            return;
        }

        RequireUrl(endpoint.BaseUrl, $"{section}.baseUrl");
        RequireText(endpoint.ModelId, $"{section}.modelId");
        RequireEnvironmentName(endpoint.ApiKeyEnv, $"{section}.apiKeyEnv", warnings);

        if (endpoint.Temperature is { } temperature)
        {
            Require(temperature is >= 0 and <= 2, $"{section}.temperature 必须在 0–2 之间");
        }

        if (endpoint.MaxOutputTokens is { } maxOutput)
        {
            Require(maxOutput > 0, $"{section}.maxOutputTokens 必须大于 0");
        }

        if (endpoint.ContextLength is { } contextLength)
        {
            Require(contextLength >= 1024, $"{section}.contextLength 不能小于 1024");
        }
    }

    private static void ValidateChannels(ChannelAccounts channels, List<string> warnings)
    {
        if (channels.Mattermost is { } mattermost)
        {
            ValidateMattermost(mattermost.Roleplay, "channels.mattermost.roleplay", warnings);
            ValidateMattermost(mattermost.Work, "channels.mattermost.work", warnings);
        }

        if (channels.WeChat is { } weChat)
        {
            RequireEnvironmentName(weChat.TokenEnv, "channels.weChat.tokenEnv", warnings);
            RequireUrl(weChat.BaseUrl, "channels.weChat.baseUrl");
            if (!string.IsNullOrWhiteSpace(weChat.CdnBaseUrl))
            {
                RequireUrl(weChat.CdnBaseUrl, "channels.weChat.cdnBaseUrl");
            }

            RequireNoBlankItems(weChat.AllowedUsers, "channels.weChat.allowedUsers");
        }
    }

    private static void ValidateMattermost(MattermostAccount account, string section, List<string> warnings)
    {
        RequireUrl(account.BaseUrl, $"{section}.baseUrl");
        RequireEnvironmentName(account.TokenEnv, $"{section}.tokenEnv", warnings);
        RequireNoBlankItems(account.AllowedUsers, $"{section}.allowedUsers");
    }

    private static void ValidateProactive(ProactiveOptions proactive)
    {
        var heartbeat = proactive.Heartbeat;
        Require(heartbeat.MinMinutes >= 1, "proactive.heartbeat.minMinutes 必须至少 1 分钟");
        Require(
            heartbeat.MaxMinutes >= heartbeat.MinMinutes,
            "proactive.heartbeat.maxMinutes 不能小于 minMinutes");
        Require(heartbeat.CooldownMinutes >= 0, "proactive.heartbeat.cooldownMinutes 不能为负");
        Require(heartbeat.HotZoneMinutes >= 0, "proactive.heartbeat.hotZoneMinutes 不能为负");
        Require(heartbeat.LongingCap >= 1, "proactive.heartbeat.longingCap 必须大于 0");
        Require(heartbeat.NormalIncreaseMin >= 0, "proactive.heartbeat.normalIncreaseMin 不能为负");
        Require(
            heartbeat.NormalIncreaseMax >= heartbeat.NormalIncreaseMin,
            "proactive.heartbeat.normalIncreaseMax 不能小于 normalIncreaseMin");
        Require(heartbeat.TriggerPenalty >= 0, "proactive.heartbeat.triggerPenalty 不能为负");
        Require(heartbeat.TriggerThreshold >= 0, "proactive.heartbeat.triggerThreshold 不能为负");
        Require(
            heartbeat.LevelFluctuationChance is >= 0 and <= 100,
            "proactive.heartbeat.levelFluctuationChance 必须在 0–100 之间");

        if (heartbeat.LevelThresholds is { Count: > 0 } thresholds)
        {
            var previous = 0;
            foreach (var value in thresholds)
            {
                Require(value > previous, "proactive.heartbeat.levelThresholds 必须严格递增且大于 0");
                previous = value;
            }
        }

        var cron = proactive.EffectiveCron;
        Require(cron.MaxJobs is >= 1 and <= 1000, "proactive.cronJobs.maxJobs 必须在 1–1000 之间");
        Require(
            cron.MaxFailureStreak is >= 1 and <= 100,
            "proactive.cronJobs.maxFailureStreak 必须在 1–100 之间");
        Require(
            cron.ScriptTimeoutSeconds is >= 1 and <= 86_400,
            "proactive.cronJobs.scriptTimeoutSeconds 必须在 1–86400 之间");
    }

    private static void ValidateTools(ToolOptions tools, List<string> warnings)
    {
        RequireNoBlankItems(tools.Disabled, "tools.disabled");
        RequireNoBlankItems(tools.DeniedPaths, "tools.deniedPaths");

        if (tools.Mcp is { Count: > 0 } servers)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in servers)
            {
                RequireText(server.Name, "tools.mcp[].name");
                Require(names.Add(server.Name.Trim()), $"tools.mcp 中服务名重复：{server.Name}");
                RequireUrl(server.Url, $"tools.mcp[{server.Name}].url");
                RequireNoBlankItems(server.AllowedTools, $"tools.mcp[{server.Name}].allowedTools");
                if (!string.IsNullOrWhiteSpace(server.TokenEnv))
                {
                    RequireEnvironmentName(server.TokenEnv!, $"tools.mcp[{server.Name}].tokenEnv", warnings);
                }
            }
        }

        if (tools.Subagents is not { } subagents)
        {
            return;
        }

        Require(
            subagents.WaitTimeoutSeconds is >= 1 and <= 3600,
            "tools.subagents.waitTimeoutSeconds 必须在 1–3600 之间");
        Require(subagents.MaxLoopIterations >= 1, "tools.subagents.maxLoopIterations 必须大于 0");

        var agentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in subagents.EffectiveAgents)
        {
            RequireText(agent.Name, "tools.subagents.agents[].name");
            Require(
                agentNames.Add(agent.Name.Trim()),
                $"tools.subagents.agents 中名字重复：{agent.Name}");
            RequireText(agent.Instructions, $"tools.subagents.agents[{agent.Name}].instructions");
            RequireNoBlankItems(agent.Tools, $"tools.subagents.agents[{agent.Name}].tools");

            if (agent.AllowShell)
            {
                warnings.Add($"子智能体 {agent.Name} 启用了免审批终端（allowShell）：子任务的审批不会回到父 agent。");
            }

            if (string.IsNullOrWhiteSpace(agent.Description))
            {
                warnings.Add($"子智能体 {agent.Name} 没有 description，父 agent 只能凭名字猜该派什么活。");
            }
        }
    }

    private static void ValidateShell(ShellOptions? shell)
    {
        if (shell is null)
        {
            return;
        }

        Require(shell.TimeoutSeconds is >= 1 and <= 3600, "shell.timeoutSeconds 必须在 1–3600 之间");
        Require(
            shell.MaxOutputBytes is >= 1024 and <= 50_000_000,
            "shell.maxOutputBytes 必须在 1024–50000000 之间");
        RequireNoBlankItems(shell.DenyPatterns, "shell.denyPatterns");

        if (!string.IsNullOrWhiteSpace(shell.WorkingDirectory))
        {
            var directory = shell.WorkingDirectory.Trim();
            Require(
                directory.StartsWith('/') || directory.StartsWith('~'),
                "shell.workingDirectory 必须是绝对路径（相对路径会按 daemon 的当前目录解析）");
        }
    }

    private static void ValidateAttachments(AttachmentOptions? attachments)
    {
        if (attachments is null)
        {
            return;
        }

        Require(
            attachments.TimeoutSeconds is >= 1 and <= 600,
            "attachments.timeoutSeconds 必须在 1–600 之间");
    }

    private static void ValidateContext(ContextOptions? context, List<string> warnings)
    {
        if (context?.Compaction is not { } compaction)
        {
            return;
        }

        Require(compaction.MaxContextWindowTokens >= 1024, "context.compaction.maxContextWindowTokens 不能小于 1024");
        Require(
            compaction.MaxOutputTokens >= 0 && compaction.MaxOutputTokens < compaction.MaxContextWindowTokens,
            "context.compaction.maxOutputTokens 必须小于 maxContextWindowTokens");
        Require(
            compaction.TriggerTokens > CompactionOptions.FloorTokens,
            $"context.compaction.triggerTokens 必须大于 {CompactionOptions.FloorTokens}（500K 以下不压）");
        Require(
            compaction.TargetTokens >= CompactionOptions.FloorTokens
            && compaction.TargetTokens < compaction.TriggerTokens,
            $"context.compaction.targetTokens 必须在 {CompactionOptions.FloorTokens} 到 triggerTokens 之间");
        Require(compaction.MinimumPreservedGroups >= 1, "context.compaction.minimumPreservedGroups 必须大于 0");
        Require(compaction.MinimumIntervalMinutes >= 0, "context.compaction.minimumIntervalMinutes 不能为负");

        if (compaction.Enabled)
        {
            var inputBudget = compaction.MaxContextWindowTokens - compaction.MaxOutputTokens;
            if (inputBudget <= CompactionOptions.FloorTokens)
            {
                warnings.Add(
                    $"上下文窗口减输出上限只剩 {inputBudget} token，不到 500K：压缩不会触发（实际等于关闭）。");
            }
        }
    }

    private static void ValidateDatabase(WorkspaceConfig config)
    {
        Require(
            string.Equals(config.DatabaseProvider, "sqlite", StringComparison.OrdinalIgnoreCase),
            "databaseProvider 目前只支持 sqlite（postgres 预留、尚未实现）");

        if (!string.IsNullOrWhiteSpace(config.DatabasePath))
        {
            Require(Path.IsPathFullyQualified(config.DatabasePath), "databasePath 必须是绝对路径");
        }
    }

    private static void ValidateProviders(WorkspaceConfig config, List<string> warnings)
    {
        if (config.Photo is { } photo)
        {
            RequireText(photo.Provider, "photo.provider");
            RequireText(photo.PromptsDir, "photo.promptsDir");
            ValidateProviderSection(photo.Providers, photo.Provider, "photo", warnings);
        }

        if (config.Voice is { } voice)
        {
            RequireText(voice.Provider, "voice.provider");
            ValidateProviderSection(voice.Providers, voice.ResolveSynthesisProvider(), "voice", warnings, "合成");
            ValidateProviderSection(voice.Providers, voice.ResolveTranscriptionProvider(), "voice", warnings, "转写");
        }

        if (config.Web is { } web)
        {
            RequireText(web.Provider, "web.provider");
            ValidateProviderSection(web.Providers, web.Provider, "web", warnings);
        }

        if (config.Notes is { } notes)
        {
            RequireText(notes.Provider, "notes.provider");
            ValidateProviderSection(notes.Providers, notes.Provider, "notes", warnings);
        }

        if (config.Notify is { } notify)
        {
            RequireText(notify.Provider, "notify.provider");
            ValidateProviderSection(notify.Providers, notify.Provider, "notify", warnings);
        }
    }

    private static void ValidateProviderSection(
        IReadOnlyDictionary<string, JsonElement>? providers,
        string provider,
        string section,
        List<string> warnings,
        string? direction = null)
    {
        var key = provider.Trim();
        if (string.Equals(key, "none", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (providers is null || !providers.TryGetValue(key, out var element))
        {
            warnings.Add(
                $"{section}.providers.{key}{(direction is null ? string.Empty : $"（{direction}）")} 未配置，将使用该供应商的默认参数。");
            return;
        }

        Require(
            element.ValueKind == JsonValueKind.Object,
            $"{section}.providers.{key} 必须是对象");
    }

    private static void RequireEnvironmentName(string? value, string field, List<string> warnings)
    {
        RequireText(value, field);
        var name = value!.Trim();
        Require(
            EnvironmentName.IsMatch(name),
            $"{field} 只能写环境变量名（形如 SHOYEE_API_KEY），不能写明文密钥");
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
        {
            warnings.Add($"{field} 引用的环境变量 {name} 当前未设置，这一项要到设置后才能用。");
        }
    }

    private static void RequireNoBlankItems(IReadOnlyList<string>? items, string field)
    {
        if (items is null)
        {
            return;
        }

        Require(items.All(static item => !string.IsNullOrWhiteSpace(item)), $"{field} 不能包含空项");
    }

    private static void RequireText(string? value, string field)
        => Require(!string.IsNullOrWhiteSpace(value), $"{field} 不能为空");

    private static void RequireUrl(string? value, string field)
    {
        RequireText(value, field);
        Require(
            Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
            $"{field} 必须是 http(s) 绝对地址");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    private static void Fail(string message)
        => throw new RpcException(RpcErrors.InvalidParams, message);
}
