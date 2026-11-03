using Shoyee.Storage.Abstractions;

namespace Shoyee.Storage;

/// <summary>首次建库时写入的默认状态（之后可增删改，不需要改代码）。</summary>
public static class StateDefaults
{
    /// <summary>默认状态定义。</summary>
    public static IReadOnlyList<StateRecord> All { get; } =
    [
        new("mood", "心情", "平静", "当前心情的短词描述，如 平静/高兴/疲惫/有点生气", true, default),
        new("energy", "精力", "0.7", "0–1 的小数；影响回复长短与主动性", true, default),
        new("longing", "思念", "0", "0–100 的整数；只由互动间隔驱动，影响心跳频率与强度", false, default),
        new("scene", "场景", "living_room", "当前所在场景，取 .comfyui/prompts/scene.json 的 key；camera 用", true, default),
        new("outfit", "服装", "home_loungewear", "当前衣装，取 .comfyui/prompts/outfit.json 的 key；camera 用", true, default),
    ];
}
