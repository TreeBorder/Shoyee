namespace Shoyee.Tools.Composition;

/// <summary>工具可见范围（写进元数据，设置界面/状态可查；专用工具必须显式标记）。</summary>
public enum ToolScope
{
    /// <summary>两种模式都可用。</summary>
    All,

    /// <summary>只给工作模式（写文件、执行命令这类需要审批的能力）。</summary>
    Work,

    /// <summary>只给 RP（角色图、语音等）。</summary>
    Roleplay,

    /// <summary>只给渠道入站场景。</summary>
    Channel,
}
