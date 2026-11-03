using System.Text.Json;
using System.Text.Json.Nodes;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Daemon.Configuration;

/// <summary>一次编辑的结果：待写入的文档 + 解析出的配置 + 提醒。</summary>
/// <param name="Document">合并/替换后的整份 JSON（<c>root</c> 已固定为工作区根）。</param>
/// <param name="Config">文档对应的配置对象。</param>
/// <param name="Warnings">不拦保存的提醒。</param>
public sealed record EditedWorkspaceConfig(
    JsonObject Document,
    WorkspaceConfig Config,
    IReadOnlyList<string> Warnings);

/// <summary>
/// <c>config.set</c> 的文档编辑：局部深合并或整份替换，然后走与启动相同的解析与校验。
///
/// 局部合并**只改点到的键**，未知字段（新版本/供应商自留的键）原样保留——
/// 结构化设置页不该因为一次保存就把文件里别的东西抹掉。
/// </summary>
public static class WorkspaceConfigEditor
{
    /// <summary>计算要写入的配置文档。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="request">局部合并或整份原文。</param>
    /// <exception cref="RpcException">参数不合法、合并后解析失败或字段校验失败（<c>invalid_params</c>）。</exception>
    public static EditedWorkspaceConfig Edit(string workspaceRoot, ConfigSetRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(request);

        var hasPatch = request.Patch is { Count: > 0 };
        var hasRaw = !string.IsNullOrWhiteSpace(request.Raw);
        if (hasPatch == hasRaw)
        {
            throw new RpcException(
                RpcErrors.InvalidParams,
                "patch 与 raw 必须二选一（patch = 局部配置，raw = 整份 config.json）");
        }

        var document = hasRaw
            ? ParseRaw(request.Raw!)
            : MergeIntoExisting(workspaceRoot, request.Patch!);

        WorkspaceConfig config;
        try
        {
            config = WorkspaceConfigLoader.FromDocument(document, workspaceRoot);
        }
        catch (DaemonStartupException ex)
        {
            throw new RpcException(RpcErrors.InvalidParams, ex.Message);
        }

        var warnings = ConfigValidator.Validate(config);
        return new EditedWorkspaceConfig(document, config, warnings);
    }

    /// <summary>整份原文：不做合并，但根节点必须是对象。</summary>
    private static JsonObject ParseRaw(string raw)
    {
        try
        {
            return JsonNode.Parse(raw) as JsonObject
                   ?? throw new RpcException(RpcErrors.InvalidParams, "raw 的根节点必须是 JSON 对象");
        }
        catch (JsonException ex)
        {
            throw new RpcException(RpcErrors.InvalidParams, $"raw 不是合法 JSON（{ex.Message}）");
        }
    }

    /// <summary>局部合并：以磁盘上现有文件为底，对象逐层合并，数组/标量整体替换，null 删除键。</summary>
    private static JsonObject MergeIntoExisting(string workspaceRoot, JsonObject patch)
    {
        var path = Path.Combine(workspaceRoot, "config.json");
        JsonObject document;
        try
        {
            document = WorkspaceConfigLoader.ReadDocument(path);
        }
        catch (DaemonStartupException ex)
        {
            throw new RpcException(RpcErrors.InvalidParams, ex.Message);
        }

        Merge(document, patch);
        return document;
    }

    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            // root 由 daemon 固定，客户端传什么都不认
            if (string.Equals(key, "root", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (value is null)
            {
                target.Remove(key);
                continue;
            }

            if (value is JsonObject patchObject && target[key] is JsonObject targetObject)
            {
                Merge(targetObject, patchObject);
                continue;
            }

            target[key] = value.DeepClone();
        }
    }
}
