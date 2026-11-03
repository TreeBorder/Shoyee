using System.Text.Json;

namespace Shoyee.Photo;

/// <summary>词表缺失或条目不存在；工具层把它转成可操作提示。</summary>
public sealed class PhotoVocabularyException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="message">给模型/用户看的原因。</param>
    public PhotoVocabularyException(string message) : base(message)
    {
    }
}

/// <summary>
/// 读 <c>.comfyui/prompts/*.json</c> 词表（场景/服装/动作/人设/环境/质量/视角）。
/// 词表是用户手工编辑的文件，**每次调用都重新读**，改完不用重启。
/// </summary>
public sealed class PhotoPrompts
{
    /// <summary>场景。</summary>
    public const string SceneKind = "scene";

    /// <summary>服装。</summary>
    public const string OutfitKind = "outfit";

    /// <summary>动作。</summary>
    public const string ActionKind = "action";

    /// <summary>人设。</summary>
    public const string CharacterKind = "character";

    /// <summary>环境（按场景分类 + 时段）。</summary>
    public const string EnvironmentKind = "environment";

    /// <summary>质量（固定取 <c>default</c>）。</summary>
    public const string QualityKind = "quality";

    /// <summary>视角（随机取一条）。</summary>
    public const string AngleKind = "angle";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _directory;

    /// <summary>创建词表读取器。</summary>
    /// <param name="directory">词表目录（绝对路径）。</param>
    public PhotoPrompts(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>词表目录。</summary>
    public string Directory => _directory;

    /// <summary>取一条词表；不存在时返回 null。</summary>
    /// <param name="kind">类别（如 <see cref="SceneKind"/>）。</param>
    /// <param name="key">条目 key。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<PromptBlock?> GetAsync(string kind, string key, CancellationToken cancellationToken = default)
    {
        var blocks = await ReadAsync(kind, cancellationToken).ConfigureAwait(false);
        return blocks.TryGetValue(key, out var block) ? block : null;
    }

    /// <summary>判断条目是否存在。</summary>
    /// <param name="kind">类别。</param>
    /// <param name="key">条目 key。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<bool> HasAsync(string kind, string key, CancellationToken cancellationToken = default)
    {
        var blocks = await ReadAsync(kind, cancellationToken).ConfigureAwait(false);
        return blocks.ContainsKey(key);
    }

    /// <summary>随机取一条（视角/缺省兜底用）。</summary>
    /// <param name="kind">类别。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<PromptBlock> GetRandomAsync(string kind, CancellationToken cancellationToken = default)
    {
        var blocks = await ReadAsync(kind, cancellationToken).ConfigureAwait(false);
        if (blocks.Count == 0)
        {
            throw new PhotoVocabularyException($"词表 {kind}.json 是空的，至少保留一条。");
        }

        return blocks.ElementAt(Random.Shared.Next(blocks.Count)).Value;
    }

    /// <summary>列出某类词表的全部条目（帮助工具用）。</summary>
    /// <param name="kind">类别。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<PromptEntry>> ListAsync(string kind, CancellationToken cancellationToken = default)
    {
        var blocks = await ReadAsync(kind, cancellationToken).ConfigureAwait(false);
        return blocks
            .Select(static pair => new PromptEntry(
                pair.Key,
                pair.Value.DisplayName ?? pair.Key,
                pair.Value.Note ?? string.Empty))
            .ToArray();
    }

    /// <summary>
    /// 按场景的 <c>environmentCategory</c> 与当前时段选环境词条：
    /// <c>{category}_{time}</c> → 早晨兜底 <c>{category}_day</c> → <c>{category}</c> → <c>default</c>。
    /// </summary>
    /// <param name="scene">场景 key。</param>
    /// <param name="now">当前时间（决定 morning/day/evening/night）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<string> ResolveEnvironmentKeyAsync(
        string scene,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var sceneBlock = await GetAsync(SceneKind, scene, cancellationToken).ConfigureAwait(false);
        var category = sceneBlock?.EnvironmentCategory;
        if (string.IsNullOrWhiteSpace(category) || string.Equals(category, "studio", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(category) ? "default" : "studio";
        }

        var environments = await ReadAsync(EnvironmentKind, cancellationToken).ConfigureAwait(false);
        var time = TimePeriod(now.Hour);

        var candidates = new List<string> { $"{category}_{time}" };
        if (time == "morning")
        {
            candidates.Add($"{category}_day");
        }

        candidates.Add(category);

        foreach (var candidate in candidates)
        {
            if (environments.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return environments.ContainsKey("default") ? "default" : string.Empty;
    }

    private static string TimePeriod(int hour) => hour switch
    {
        >= 6 and < 11 => "morning",
        >= 11 and < 17 => "day",
        >= 17 and < 20 => "evening",
        _ => "night",
    };

    private async Task<Dictionary<string, PromptBlock>> ReadAsync(string kind, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_directory, kind + ".json");
        if (!File.Exists(path))
        {
            throw new PhotoVocabularyException(
                $"词表文件不存在：{path}。首次启动会把模板复制到工作区的 .comfyui/prompts/，"
                + "也可以自己按同类格式补一份。");
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new PhotoVocabularyException($"词表文件是空的：{path}。");
        }

        try
        {
            var blocks = JsonSerializer.Deserialize<Dictionary<string, PromptBlock>>(json, Json);
            if (blocks is null)
            {
                throw new PhotoVocabularyException($"词表文件格式不对（应为 key → 词条的对象）：{path}。");
            }

            return blocks;
        }
        catch (JsonException ex)
        {
            throw new PhotoVocabularyException($"词表文件 JSON 解析失败：{path}（{ex.Message}）。");
        }
    }
}
