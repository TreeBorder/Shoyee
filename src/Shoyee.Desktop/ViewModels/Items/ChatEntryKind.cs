namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>占位消息的展示类别（真实数据按 MessageKind 映射）。</summary>
public enum ChatEntryKind
{
    /// <summary>用户发言。</summary>
    User,

    /// <summary>助手正文。</summary>
    Assistant,

    /// <summary>独立成泡的 Markdown 图片。</summary>
    Image,

    /// <summary>正文里本地文件链接拆出来的文件气泡。</summary>
    File,

    /// <summary>工具过程行。</summary>
    Tool,

    /// <summary>模型的思考过程。</summary>
    Reasoning,

    /// <summary>系统提示 / 分隔。</summary>
    System,

    /// <summary>系统注入（心跳 / 定时 / 后台回执）：像思考/工具一样折叠成过程行，默认隐藏。</summary>
    SystemInput,
}
