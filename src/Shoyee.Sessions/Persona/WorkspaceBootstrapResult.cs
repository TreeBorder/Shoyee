namespace Shoyee.Sessions.Persona;

/// <summary>工作区模板补齐结果。</summary>
/// <param name="CreatedFiles">本次新建的文件（相对工作区根）；已存在的不在其中。</param>
/// <param name="Messages">给人看的说明。</param>
public sealed record WorkspaceBootstrapResult(
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> Messages);
