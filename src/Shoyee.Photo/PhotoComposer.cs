namespace Shoyee.Photo;

/// <summary>
/// 把「场景 + 服装 + 动作」拼成生成请求（照 Harem <c>WorkflowBuilderService</c> 的顺序）：
/// 质量 → 环境 → 场景 → 视角 → 人设 → 服装 → 动作；正/负词各自去重。
/// 环境按场景的 <c>environmentCategory</c> + 当前时段选；视角每次随机。
///
/// 顺序是刻意的（先定画质与光线，再定人在哪、怎么拍、穿什么），改顺序会改变出图风格。
/// </summary>
public sealed class PhotoComposer
{
    private readonly PhotoPrompts _prompts;
    private readonly string? _character;

    /// <summary>创建组装器。</summary>
    /// <param name="prompts">词表。</param>
    /// <param name="character">人设 key（<c>character.json</c> 的键）；空 = 未配置。</param>
    public PhotoComposer(PhotoPrompts prompts, string? character)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        _prompts = prompts;
        _character = string.IsNullOrWhiteSpace(character) ? null : character.Trim();
    }

    /// <summary>组装一次请求。</summary>
    /// <param name="plan">取景。</param>
    /// <param name="now">当前时间（决定环境时段）。</param>
    /// <param name="seed">指定 seed；null = 随机。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<PhotoRequest> ComposeAsync(
        PhotoPlan plan,
        DateTimeOffset now,
        int? seed = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (_character is null)
        {
            throw new PhotoVocabularyException(
                "尚未配置角色图人设：在 config.json 的 photo.character 里填 character.json 的某个 key。");
        }

        var quality = await RequireAsync(PhotoPrompts.QualityKind, "default", cancellationToken).ConfigureAwait(false);
        var scene = await RequireAsync(PhotoPrompts.SceneKind, plan.Scene, cancellationToken).ConfigureAwait(false);
        var outfit = await RequireAsync(PhotoPrompts.OutfitKind, plan.Outfit, cancellationToken).ConfigureAwait(false);
        var action = await RequireAsync(PhotoPrompts.ActionKind, plan.Action, cancellationToken).ConfigureAwait(false);
        var character = await RequireAsync(PhotoPrompts.CharacterKind, _character, cancellationToken).ConfigureAwait(false);
        var angle = await _prompts.GetRandomAsync(PhotoPrompts.AngleKind, cancellationToken).ConfigureAwait(false);

        var environmentKey = await _prompts
            .ResolveEnvironmentKeyAsync(plan.Scene, now, cancellationToken)
            .ConfigureAwait(false);
        var environment = string.IsNullOrEmpty(environmentKey)
            ? null
            : await _prompts.GetAsync(PhotoPrompts.EnvironmentKind, environmentKey, cancellationToken)
                .ConfigureAwait(false);

        var positive = new List<string>();
        var negative = new List<string>();
        void Append(PromptBlock? block)
        {
            if (block is null)
            {
                return;
            }

            positive.AddRange(block.PositivePrompts);
            negative.AddRange(block.NegativePrompts);
        }

        Append(quality);
        Append(environment);
        Append(scene);
        Append(angle);
        Append(character);
        Append(outfit);
        Append(action);

        var (width, height) = PhotoAspectRatios.Resolve(plan.AspectRatio);
        return new PhotoRequest(
            string.Join(", ", positive.Distinct(StringComparer.Ordinal)),
            string.Join(", ", negative.Distinct(StringComparer.Ordinal)),
            width,
            height,
            seed ?? Random.Shared.Next(1, int.MaxValue),
            _character);
    }

    private async Task<PromptBlock> RequireAsync(string kind, string key, CancellationToken cancellationToken)
    {
        var block = await _prompts.GetAsync(kind, key, cancellationToken).ConfigureAwait(false);
        return block ?? throw new PhotoVocabularyException(
            $"词表 {kind}.json 里没有「{key}」，先用对应的 get_support_* 工具看可用值。");
    }
}
