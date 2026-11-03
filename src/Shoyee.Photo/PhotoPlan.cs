namespace Shoyee.Photo;

/// <summary>角色图的一次取景：场景、服装、动作、比例（全部来自词表 key）。</summary>
/// <param name="Scene">场景 key。</param>
/// <param name="Outfit">服装 key。</param>
/// <param name="Action">动作 key。</param>
/// <param name="AspectRatio">比例（<c>2:3</c> / <c>3:2</c> / <c>1:1</c>）。</param>
public sealed record PhotoPlan(
    string Scene,
    string Outfit,
    string Action,
    string? AspectRatio = null);
