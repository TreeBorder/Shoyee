using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Shoyee.Desktop.ViewModels;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.Views;

/// <summary>工作模式：聊天 + 可折叠状态栏（占位骨架）。</summary>
public partial class WorkView : UserControl
{
    private INotifyCollectionChanged? _messages;
    private bool _scrollQueued;

    /// <summary>创建视图。</summary>
    public WorkView()
    {
        InitializeComponent();

        // 隧道阶段先判断发送快捷键，避免 TextBox 先把 Enter 当成换行吃掉
        ComposerBox.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);

        Loaded += (_, _) => ScrollToEnd();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                ScrollToEnd();
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (_messages is not null)
            {
                _messages.CollectionChanged -= OnMessagesChanged;
            }

            _messages = (DataContext as WorkViewModel)?.VisibleMessages;
            if (_messages is not null)
            {
                _messages.CollectionChanged += OnMessagesChanged;
                TrackItems();
            }
        };
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TrackItems();
        ScrollToEnd();
    }

    /// <summary>流式正文追加时气泡只在长高、不增删集合项；单独盯 Text，才能在底部时跟着滚。</summary>
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

        // 用户往上翻历史时不强行拉回底部
        var remaining = MessageScroller.Extent.Height - MessageScroller.Viewport.Height - MessageScroller.Offset.Y;
        if (remaining <= 120)
        {
            ScrollToEnd();
        }
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not WorkViewModel viewModel || e.Key != Key.Enter)
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
                MessageScroller.ScrollToEnd();
            },
            DispatcherPriority.Background);
    }
}
