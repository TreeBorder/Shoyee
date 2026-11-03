using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Rpc;
using Shoyee.Photo;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Media;

namespace Shoyee.Tools.Photo;

/// <summary>
/// 图片工具面：
/// <list type="bullet">
///   <item>工作模式 <c>image_generate</c>：直接按提示词生图，等到出图返回路径（工作活可以等）。</item>
///   <item>RP 模式 <c>camera</c> / <c>gallery</c> / <c>random_outfit</c> / <c>get_support_*</c>：
///     与 Harem 同语义——提交后立即返回，后台出图完成再通过 <see cref="IPhotoDelivery"/> 推给用户。</item>
/// </list>
/// 场景/服装存在通用状态表（<c>scene</c> / <c>outfit</c>），由她自己用 update_state 维护；
/// 词表是工作区里可编辑的 <c>.comfyui/prompts/*.json</c>。
/// </summary>
public static class PhotoTools
{
    /// <summary>工作模式通用生图。</summary>
    public const string GenerateToolName = "image_generate";

    /// <summary>RP 拍照。</summary>
    public const string CameraToolName = "camera";

    /// <summary>RP 相册（指定场景/服装/动作）。</summary>
    public const string GalleryToolName = "gallery";

    /// <summary>RP 随机换装。</summary>
    public const string RandomOutfitToolName = "random_outfit";

    /// <summary>场景清单。</summary>
    public const string SupportSceneToolName = "get_support_scene";

    /// <summary>服装清单。</summary>
    public const string SupportOutfitToolName = "get_support_outfit";

    /// <summary>动作清单。</summary>
    public const string SupportActionToolName = "get_support_action";

    /// <summary>场景状态 key。</summary>
    public const string SceneStateKey = "scene";

    /// <summary>服装状态 key。</summary>
    public const string OutfitStateKey = "outfit";

    /// <summary>构建图片工具面（没配供应商时为空）。</summary>
    /// <param name="service">出图服务（null = 不挂工具）。</param>
    /// <param name="prompts">词表（null = 不挂工具）。</param>
    /// <param name="tasks">异步出图队列。</param>
    /// <param name="states">状态仓库（场景/服装）。</param>
    public static IReadOnlyList<ToolDescriptor> Create(
        PhotoService? service,
        PhotoPrompts? prompts,
        PhotoTaskRunner? tasks,
        IStateRepository? states)
    {
        var descriptors = new List<ToolDescriptor>(7);

        if (service is not null)
        {
            descriptors.Add(CreateGenerate(service));
        }

        if (prompts is not null && tasks is not null && states is not null)
        {
            descriptors.Add(CreateCamera(prompts, tasks, states));
            descriptors.Add(CreateGallery(prompts, tasks));
            descriptors.Add(CreateRandomOutfit(prompts, states));
            descriptors.Add(CreateSupport(
                SupportSceneToolName,
                PhotoPrompts.SceneKind,
                "场景",
                prompts));
            descriptors.Add(CreateSupport(
                SupportOutfitToolName,
                PhotoPrompts.OutfitKind,
                "服装",
                prompts));
            descriptors.Add(CreateSupport(
                SupportActionToolName,
                PhotoPrompts.ActionKind,
                "动作",
                prompts));
        }

        return descriptors;
    }

    private static ToolDescriptor CreateGenerate(PhotoService service)
    {
        var tool = AIFunctionFactory.Create(
            async (
                [Description("画面描述（英文提示词效果最好）。")] string prompt,
                [Description("负向提示词（可选），不想出现的东西。")] string? negative = null,
                [Description("比例（可选）：2:3 竖屏（默认）、3:2 横屏、1:1 方图。")] string? aspectRatio = null,
                CancellationToken cancellationToken = default) =>
            {
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    return "生图失败：提示词为空。";
                }

                var (width, height) = PhotoAspectRatios.Resolve(aspectRatio);
                try
                {
                    var assets = await service.GenerateAsync(
                            new PhotoRequest(
                                prompt.Trim(),
                                negative?.Trim() ?? string.Empty,
                                width,
                                height,
                                Random.Shared.Next(1, int.MaxValue)),
                            cancellationToken)
                        .ConfigureAwait(false);

                    // 挂到本轮回复上（这是同步工具，图已经落盘）
                    foreach (var asset in assets)
                    {
                        OutboundScope.Current?.Add(AttachmentFactory.FromFile(asset.Path, asset.Mime));
                    }

                    return "已生成图片：\n" + string.Join(
                        "\n",
                        assets.Select(static a => $"- {a.Path}（{a.Mime}，{a.SizeBytes / 1024.0:F0} KB）"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "生图失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = GenerateToolName,
                Description = "按提示词生成一张图片，返回本地绝对路径（会等到出图完成，通常几十秒到几分钟）。"
                              + "适合做配图、示意图、素材；角色本人的照片用 RP 的 camera/gallery。",
            });

        return ToolDescriptor.Create(tool, ToolScope.Work, writes: true, requiresApproval: false);
    }

    private static ToolDescriptor CreateCamera(PhotoPrompts prompts, PhotoTaskRunner tasks, IStateRepository states)
    {
        var tool = AIFunctionFactory.Create(
            async (
                [Description("动作 key（先用 get_support_action 看可用值）。")] string action,
                [Description("比例（可选）：2:3 竖屏（默认）、3:2 横屏。")] string? aspectRatio = null,
                CancellationToken cancellationToken = default) =>
            {
                var laneId = PhotoScope.LaneId;
                if (string.IsNullOrWhiteSpace(laneId))
                {
                    return "拍照失败：当前不在角色扮演会话里。";
                }

                try
                {
                    var scene = await ReadStateAsync(states, SceneStateKey, "场景", "bedroom", cancellationToken)
                        .ConfigureAwait(false);
                    var outfit = await ReadStateAsync(states, OutfitStateKey, "服装", "casual", cancellationToken)
                        .ConfigureAwait(false);
                    if (scene.Error is not null)
                    {
                        return "拍照失败：" + scene.Error;
                    }

                    if (outfit.Error is not null)
                    {
                        return "拍照失败：" + outfit.Error;
                    }

                    if (!await prompts.HasAsync(PhotoPrompts.SceneKind, scene.Value!, cancellationToken).ConfigureAwait(false))
                    {
                        return $"拍照失败：词表里没有场景「{scene.Value}」，先用 {SupportSceneToolName} 看可用值。";
                    }

                    if (!await prompts.HasAsync(PhotoPrompts.OutfitKind, outfit.Value!, cancellationToken).ConfigureAwait(false))
                    {
                        return $"拍照失败：词表里没有服装「{outfit.Value}」，先用 {SupportOutfitToolName} 看可用值。";
                    }

                    if (!await prompts.HasAsync(PhotoPrompts.ActionKind, action, cancellationToken).ConfigureAwait(false))
                    {
                        return $"拍照失败：词表里没有动作「{action}」，先用 {SupportActionToolName} 看可用值。";
                    }

                    // 不传 caption：IM 文件消息会把 caption 显示成图前面一行字，直接发图更干净
                    // 把任务 id 带回工具结果：桌面据此把异步出的图挂回这一行 camera 调用。
                    var taskId = tasks.Enqueue(
                        new PhotoPlan(scene.Value!, outfit.Value!, action, PhotoAspectRatios.Normalize(aspectRatio)),
                        laneId);

                    return $"已拍摄并发送照片，照片发送会有几分钟延迟（任务 {taskId}）";
                }
                catch (PhotoVocabularyException ex)
                {
                    return "拍照失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = CameraToolName,
                Description = "按当前场景和服装拍一张角色照片并发给用户（异步，几分钟后到）。"
                              + "调用前先用 update_state 维护 scene / outfit；动作可选值见 get_support_action。",
            });

        return ToolDescriptor.Create(tool, ToolScope.Roleplay, writes: true, requiresApproval: false);
    }

    private static ToolDescriptor CreateGallery(PhotoPrompts prompts, PhotoTaskRunner tasks)
    {
        var tool = AIFunctionFactory.Create(
            async (
                [Description("场景 key（先用 get_support_scene 看可用值）。")] string scene,
                [Description("服装 key（先用 get_support_outfit 看可用值）。")] string outfit,
                [Description("动作 key（先用 get_support_action 看可用值）。")] string action,
                [Description("比例（可选）：2:3 竖屏（默认）、3:2 横屏。")] string? aspectRatio = null,
                CancellationToken cancellationToken = default) =>
            {
                var laneId = PhotoScope.LaneId;
                if (string.IsNullOrWhiteSpace(laneId))
                {
                    return "相册发送失败：当前不在角色扮演会话里。";
                }

                try
                {
                    if (!await prompts.HasAsync(PhotoPrompts.SceneKind, scene, cancellationToken).ConfigureAwait(false))
                    {
                        return $"相册发送失败：词表里没有场景「{scene}」，先用 {SupportSceneToolName} 看可用值。";
                    }

                    if (!await prompts.HasAsync(PhotoPrompts.OutfitKind, outfit, cancellationToken).ConfigureAwait(false))
                    {
                        return $"相册发送失败：词表里没有服装「{outfit}」，先用 {SupportOutfitToolName} 看可用值。";
                    }

                    if (!await prompts.HasAsync(PhotoPrompts.ActionKind, action, cancellationToken).ConfigureAwait(false))
                    {
                        return $"相册发送失败：词表里没有动作「{action}」，先用 {SupportActionToolName} 看可用值。";
                    }

                    var taskId = tasks.Enqueue(
                        new PhotoPlan(scene, outfit, action, PhotoAspectRatios.Normalize(aspectRatio)),
                        laneId);

                    return $"已从相册发送照片，照片发送会有几分钟延迟（任务 {taskId}）";
                }
                catch (PhotoVocabularyException ex)
                {
                    return "相册发送失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = GalleryToolName,
                Description = "按指定场景、服装和动作从相册发一张照片（异步，几分钟后到）。"
                              + "三者的可用值分别看 get_support_scene / get_support_outfit / get_support_action。",
            });

        return ToolDescriptor.Create(tool, ToolScope.Roleplay, writes: true, requiresApproval: false);
    }

    private static ToolDescriptor CreateRandomOutfit(PhotoPrompts prompts, IStateRepository states)
    {
        var tool = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken = default) =>
            {
                try
                {
                    var entries = await prompts.ListAsync(PhotoPrompts.OutfitKind, cancellationToken)
                        .ConfigureAwait(false);
                    if (entries.Count == 0)
                    {
                        return "衣柜空空如也，没有可更换的服装。";
                    }

                    var picked = entries[Random.Shared.Next(entries.Count)];
                    var updated = await states
                        .SetAsync(OutfitStateKey, picked.Key, byAgent: true, cancellationToken)
                        .ConfigureAwait(false);
                    if (!updated)
                    {
                        return $"换装失败：状态 {OutfitStateKey} 不存在或不允许她修改。";
                    }

                    return $"随机换装完成！当前服装：{picked.Name}（{picked.Key}）。请根据这套衣服的风格选合适的场景。";
                }
                catch (PhotoVocabularyException ex)
                {
                    return "换装失败：" + ex.Message;
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = RandomOutfitToolName,
                Description = "随机换一套服装并更新 outfit 状态。换完应根据衣服风格调整场景与互动。",
            });

        return ToolDescriptor.Create(tool, ToolScope.Roleplay, writes: true, requiresApproval: false);
    }

    private static ToolDescriptor CreateSupport(string name, string kind, string label, PhotoPrompts prompts)
    {
        var tool = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken = default) =>
            {
                try
                {
                    var entries = await prompts.ListAsync(kind, cancellationToken).ConfigureAwait(false);
                    return JsonSerializer.Serialize(entries, JsonWire.Options);
                }
                catch (PhotoVocabularyException ex)
                {
                    return JsonSerializer.Serialize(new { Message = ex.Message }, JsonWire.Options);
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = $"列出支持的{label}（key / 展示名 / 说明）。调用 camera/gallery 前不确定可选值时先看这里。",
            });

        return ToolDescriptor.Create(tool, ToolScope.Roleplay, writes: false, requiresApproval: false);
    }

    private static async Task<(string? Value, string? Error)> ReadStateAsync(
        IStateRepository states,
        string key,
        string label,
        string example,
        CancellationToken cancellationToken)
    {
        var state = await states.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (state is null || string.IsNullOrWhiteSpace(state.Value))
        {
            return (null, $"还没有维护{label}状态。先用 update_state 把 {key} 设成一个词表 key（例如 {example}）。");
        }

        return (state.Value, null);
    }
}
