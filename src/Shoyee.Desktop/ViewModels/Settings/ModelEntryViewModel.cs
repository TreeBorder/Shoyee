using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Desktop.Theming;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 「模型配置」里的一条接入模型：端点、模型名、key 环境变量、上下文长度与适用场景。
///
/// 列表只负责展示；新增与编辑都在弹窗里进行，弹窗保存后写回
/// <see cref="ModelCatalogViewModel"/>。场景勾选变化会影响上方场景下拉的可选项。
/// </summary>
public partial class ModelEntryViewModel : ObservableObject
{
    /// <summary>创建一条模型。</summary>
    /// <param name="id">本机稳定标识。</param>
    /// <param name="name">显示名。</param>
    /// <param name="baseUrl">OpenAI 兼容端点。</param>
    /// <param name="model">模型 id。</param>
    /// <param name="keyEnv">API key 的环境变量名。</param>
    /// <param name="contextLength">上下文长度（token）。</param>
    /// <param name="scenes">适用场景。</param>
    /// <param name="isNew">是否是还没保存的草稿。</param>
    public ModelEntryViewModel(
        string id,
        string name,
        string baseUrl,
        string model,
        string keyEnv,
        int contextLength,
        IEnumerable<ModelScene> scenes,
        bool isNew = false)
    {
        Id = id;
        Name = name;
        BaseUrl = baseUrl;
        Model = model;
        KeyEnv = keyEnv;
        ContextLength = contextLength;
        var sceneSet = scenes.ToHashSet();
        IsChat = sceneSet.Contains(ModelScene.Chat);
        IsSmall = sceneSet.Contains(ModelScene.Small);
        IsVision = sceneSet.Contains(ModelScene.Vision);
        IsNew = isNew;
    }

    /// <summary>本机稳定标识（场景绑定用，不发给后端）。</summary>
    public string Id { get; }

    /// <summary>所属的模型配置（行内按钮的命令转给它）。</summary>
    internal ModelCatalogViewModel? Owner { get; set; }

    /// <summary>是否是还没保存的草稿。</summary>
    public bool IsNew { get; set; }

    /// <summary>显示名。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Name { get; set; }

    /// <summary>OpenAI 兼容端点。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointLabel))]
    public partial string BaseUrl { get; set; }

    /// <summary>模型 id。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointLabel))]
    public partial string Model { get; set; }

    /// <summary>API key 的环境变量名。</summary>
    [ObservableProperty]
    public partial string KeyEnv { get; set; }

    /// <summary>上下文长度（token）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextLengthLabel))]
    public partial int ContextLength { get; set; }

    /// <summary>是否承担主模型场景。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScenesLabel))]
    public partial bool IsChat { get; set; }

    /// <summary>是否承担小模型场景。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScenesLabel))]
    public partial bool IsSmall { get; set; }

    /// <summary>是否承担视觉模型场景。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScenesLabel))]
    public partial bool IsVision { get; set; }

    /// <summary>保存时的字段错误；空 = 没错误。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    /// <summary>是否有字段错误可展示。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>列表里显示的模型名；空白时回落到模型 id。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Model : Name.Trim();

    /// <summary>端点 + 模型 id 的一行摘要。</summary>
    public string EndpointLabel => $"{BaseUrl} · {Model}";

    /// <summary>适用场景的短标签。</summary>
    public string ScenesLabel
    {
        get
        {
            var scenes = new List<string>(3);
            if (IsChat)
            {
                scenes.Add("主模型");
            }

            if (IsSmall)
            {
                scenes.Add("小模型");
            }

            if (IsVision)
            {
                scenes.Add("视觉");
            }

            return scenes.Count == 0 ? "未选场景" : string.Join(" · ", scenes);
        }
    }

    /// <summary>上下文长度的短说明。</summary>
    public string ContextLengthLabel => ContextLength >= 1_000_000
        ? $"约 {ContextLength / 1_000_000.0:0.##}M tokens"
        : $"约 {ContextLength / 1000.0:0.#}k tokens";

    /// <summary>当前勾选的场景。</summary>
    public IReadOnlyList<ModelScene> Scenes
    {
        get
        {
            var scenes = new List<ModelScene>(3);
            if (IsChat)
            {
                scenes.Add(ModelScene.Chat);
            }

            if (IsSmall)
            {
                scenes.Add(ModelScene.Small);
            }

            if (IsVision)
            {
                scenes.Add(ModelScene.Vision);
            }

            return scenes;
        }
    }

    /// <summary>把当前字段导出成持久化记录。</summary>
    /// <returns>一条模型配置。</returns>
    public ModelEntrySettings ToSettings() => new(
        Id,
        DisplayName,
        string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.deepseek.com" : BaseUrl.Trim(),
        string.IsNullOrWhiteSpace(Model) ? "deepseek-flash" : Model.Trim(),
        string.IsNullOrWhiteSpace(KeyEnv) ? "DEEPSEEK_API_KEY" : KeyEnv.Trim(),
        Math.Clamp(ContextLength, 4_096, 4_000_000),
        Scenes);

    /// <summary>复制一份用于编辑弹窗，保存前不改动列表里的原值。</summary>
    /// <returns>字段相同的副本。</returns>
    public ModelEntryViewModel CloneForEdit() => new(
        Id,
        Name,
        BaseUrl,
        Model,
        KeyEnv,
        ContextLength,
        Scenes);

    /// <summary>把弹窗里改完的值写回这条记录。</summary>
    /// <param name="source">编辑副本。</param>
    public void ApplyFrom(ModelEntryViewModel source)
    {
        Name = source.Name;
        BaseUrl = source.BaseUrl;
        Model = source.Model;
        KeyEnv = source.KeyEnv;
        ContextLength = source.ContextLength;
        IsChat = source.IsChat;
        IsSmall = source.IsSmall;
        IsVision = source.IsVision;
        Error = null;
    }

    /// <summary>校验字段；返回 false 时错误写进 <see cref="Error"/>。</summary>
    /// <returns>是否可以保存。</returns>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "先给这个模型起个名字，列表里靠它区分。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(BaseUrl)
            || !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Error = "base_url 需要是 http(s) 绝对地址。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            Error = "模型 id 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(KeyEnv))
        {
            Error = "key 只存环境变量名，不能留空。";
            return false;
        }

        if (Scenes.Count == 0)
        {
            Error = "至少勾选一个适用场景，否则这个模型不会被用到。";
            return false;
        }

        Error = null;
        return true;
    }

    /// <summary>把一条持久化记录转成可编辑项。</summary>
    /// <param name="settings">模型记录。</param>
    /// <returns>列表项。</returns>
    public static ModelEntryViewModel FromSettings(ModelEntrySettings settings) => new(
        settings.Id,
        settings.Name,
        settings.BaseUrl,
        settings.Model,
        settings.KeyEnv,
        settings.ContextLength,
        settings.Scenes);

    /// <summary>打开编辑弹窗。</summary>
    [RelayCommand]
    private void Edit() => Owner?.EditCommand.Execute(this);

    /// <summary>删除这条模型。</summary>
    [RelayCommand]
    private void Remove() => Owner?.DeleteCommand.Execute(this);
}
