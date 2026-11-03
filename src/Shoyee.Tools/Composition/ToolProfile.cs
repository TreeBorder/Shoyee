namespace Shoyee.Tools.Composition;

/// <summary>工具组装档位：工作模式用全量，RP 只用只读能力。</summary>
public enum ToolProfile
{
    /// <summary>工作模式（HarnessAgent，支持审批在环）。</summary>
    Work,

    /// <summary>RP（普通 ChatClientAgent，没有审批中间件，因此只挂只读工具）。</summary>
    Roleplay,
}
