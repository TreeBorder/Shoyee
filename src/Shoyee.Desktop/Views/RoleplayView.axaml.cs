using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.ComponentModel;
using Shoyee.Desktop.ViewModels;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Views;

/// <summary>角色模式：立绘舞台 / 气泡对话（占位骨架）。</summary>
public partial class RoleplayView : UserControl
{
    private System.Collections.Specialized.INotifyCollectionChanged? _messages;
    private RoleplayViewModel? _viewModel;
    private bool _scrollQueued;

    /// <summary>创建视图。</summary>
    public RoleplayView()
    {
        InitializeComponent();

        // 隧道阶段先判断发送快捷键，避免 TextBox 先把 Enter 当成换行吃掉
        PortraitComposerBox.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        RpComposerBox.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);

        Loaded += (_, _) =>
        {
            UpdatePortraitSize();
            ScrollToEnd();
        };
        SizeChanged += (_, _) => UpdatePortraitSize();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                UpdatePortraitSize();
                ScrollToEnd();
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (_messages is not null)
            {
                _messages.CollectionChanged -= OnMessagesChanged;
            }

            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as RoleplayViewModel;
            _messages = _viewModel?.VisibleMessages;
            if (_messages is not null)
            {
                _messages.CollectionChanged += OnMessagesChanged;
                TrackItems();
            }

            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        TrackItems();
        ScrollToEnd();
    }

    /// <summary>流式正文追加时只长高、不增删集合项；盯 Text 才能在底部时跟着滚。</summary>
    private void TrackItems()
    {
        if (_messages is not System.Collections.IEnumerable items)
        {
            return;
        }

        foreach (var item in items.OfType<ChatMessageItem>())
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            item.PropertyChanged += OnItemPropertyChanged;
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatMessageItem.Text))
        {
            return;
        }

        var remaining = RpMessageScroller.Extent.Height - RpMessageScroller.Viewport.Height - RpMessageScroller.Offset.Y;
        if (remaining <= 120)
        {
            ScrollToEnd();
        }
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RoleplayViewModel viewModel || e.Key != Key.Enter)
        {
            return;
        }

        var isSend = viewModel.SendOnEnter
            ? e.KeyModifiers == KeyModifiers.None
            : e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (!isSend)
        {
            return;
        }

        e.Handled = true;
        viewModel.SendCommand.Execute(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RoleplayViewModel.PortraitScale))
        {
            UpdatePortraitSize();
        }
    }

    private void ScrollToEnd()
    {
        if (_scrollQueued)
        {
            return;
        }

        _scrollQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _scrollQueued = false;
                RpMessageScroller.ScrollToEnd();
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// 立绘按窗口自适应：
    /// - 高度/宽度取能放下的较小值，保持原图比例；
    /// - 尺寸乘 <see cref="RoleplayViewModel.PortraitScale"/>（设置项）；
    /// - 中心位置按窗口宽高比取 38%–45%，横屏更偏左、竖屏接近居中；
    /// - 顶部留 64、底部留 150 给悬浮条与对话框。
    /// </summary>
    private void UpdatePortraitSize()
    {
        if (PortraitImage?.Source is not { } source || source.Size.Height <= 0)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < 100 || height < 100)
        {
            return;
        }

        const double sidePadding = 64;
        const double topReserve = 64;
        const double bottomReserve = 150;

        var aspect = (double)source.Size.Width / source.Size.Height;
        var availableWidth = Math.Max(220, width - sidePadding * 2);
        var availableHeight = Math.Max(300, height - topReserve - bottomReserve);
        var baseHeight = Math.Min(availableHeight, availableWidth / aspect);

        var scale = Math.Clamp(_viewModel?.PortraitScale ?? 1.0, 0.5, 1.5);
        var portraitHeight = Math.Min(baseHeight * scale, availableHeight);
        var portraitWidth = portraitHeight * aspect;

        // 横屏（宽高比大）时立绘中心更偏左；竖屏接近居中
        var windowRatio = width / height;
        var centerRatio = windowRatio >= 1.4
            ? 0.38
            : windowRatio <= 1.0
                ? 0.45
                : 0.45 - (windowRatio - 1.0) / 0.4 * 0.07;

        var center = width * centerRatio;
        var left = Math.Clamp(
            center - portraitWidth / 2,
            sidePadding,
            Math.Max(sidePadding, width - sidePadding - portraitWidth));

        PortraitImage.Width = portraitWidth;
        PortraitImage.Height = portraitHeight;
        PortraitImage.Margin = new Thickness(left, 0, 0, bottomReserve);
    }
}
