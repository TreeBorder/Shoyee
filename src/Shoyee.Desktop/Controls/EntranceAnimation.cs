using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Shoyee.Desktop.Controls;

/// <summary>
/// 消息气泡入场动画：新行从下方 6px 淡入。只在控件挂到可视树时跑一次，
/// 不做逐 token 打字机效果（会和流式增量抢同一段正文，反而更跳）。
/// </summary>
public sealed class EntranceAnimation : AvaloniaObject
{
    /// <summary>是否对该控件启用入场动画。</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<EntranceAnimation, Control, bool>("Enabled");

    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);

    static EntranceAnimation()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(static (control, args) =>
        {
            if (args.NewValue is true)
            {
                Play(control);
            }
        });
    }

    /// <summary>读取是否启用。</summary>
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    /// <summary>设置是否启用。</summary>
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private static void Play(Control control)
    {
        control.Opacity = 0;
        control.RenderTransform = new TranslateTransform(0, 6);
        control.Transitions =
        [
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = Duration,
            },
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = Duration,
                Easing = new CubicEaseOut(),
            },
        ];

        // 等这一帧布局完成再切到终态，Transitions 才会真正播出来。
        Dispatcher.UIThread.Post(
            () =>
            {
                control.Opacity = 1;
                control.RenderTransform = TransformOperations.Identity;
            },
            DispatcherPriority.Render);
    }
}
