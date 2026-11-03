namespace Shoyee.Photo;

/// <summary>一个待出图任务。</summary>
/// <param name="Plan">取景。</param>
/// <param name="Context">去向。</param>
public sealed record PhotoJob(PhotoPlan Plan, PhotoTaskContext Context);
