namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>状态条（心情 / 精力 / 思念）的占位项。</summary>
public sealed class StateItem
{
    /// <summary>展示名。</summary>
    public required string Name { get; init; }

    /// <summary>显示值。</summary>
    public required string Value { get; init; }

    /// <summary>进度（0–100）；用于数值型状态。</summary>
    public double Percent { get; init; }

    /// <summary>是否数值型（否则只显示短句，不画进度条）。</summary>
    public bool IsNumeric { get; init; }

    /// <summary>强调色是否用薄荷绿。</summary>
    public bool IsMint { get; init; }

    /// <summary>强调色是否用暖金。</summary>
    public bool IsGold { get; init; }
}
