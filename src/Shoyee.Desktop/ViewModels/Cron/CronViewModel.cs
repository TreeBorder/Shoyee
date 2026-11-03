using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Client.Rpc;
using Shoyee.Desktop.Rpc;

namespace Shoyee.Desktop.ViewModels.Cron;

/// <summary>
/// 定时任务页：左侧任务列表，右侧详情与最近执行。
///
/// 任务只能由模型用 cron 工具创建；桌面只做查看、启停和删除（删除前要二次确认）。
/// 数据走工作连接的 <c>cron.list</c> / <c>cron.get</c> / <c>cron.setEnabled</c> / <c>cron.remove</c>。
/// </summary>
public partial class CronViewModel : ViewModelBase
{
    private ICronGateway? _gateway;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _detailCts;

    /// <summary>当前是否连着 daemon。</summary>
    public bool IsLive => _gateway is { IsConnected: true };

    /// <summary>任务列表（按下次触发时间排序）。</summary>
    public ObservableCollection<CronJobItem> Jobs { get; } = [];

    /// <summary>选中任务的最近执行记录。</summary>
    public ObservableCollection<CronExecutionItem> Executions { get; } = [];

    /// <summary>当前选中的任务。</summary>
    [ObservableProperty]
    public partial CronJobItem? SelectedJob { get; set; }

    /// <summary>是否正在拉取列表。</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>最近一次错误（空 = 没有错误）。</summary>
    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    /// <summary>是否正在等删除确认。</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    /// <summary>是否有错误提示。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>是否有任务。</summary>
    public bool HasJobs => Jobs.Count > 0;

    /// <summary>是否为空（加载完成后）。</summary>
    public bool IsEmpty => !IsLoading && Jobs.Count == 0;

    /// <summary>是否选中了任务。</summary>
    public bool HasSelection => SelectedJob is not null;

    /// <summary>是否有执行记录。</summary>
    public bool HasExecutions => Executions.Count > 0;

    /// <summary>列表上方的说明。</summary>
    public string HeaderSubtitle => Jobs.Count == 0
        ? "工作模式的定时任务 · 由模型用 cron 工具创建"
        : $"{Jobs.Count} 个工作模式任务 · 执行结果只进台账，要主动提醒需 deliver=push";

    /// <summary>连上 daemon 后挂上网关并拉列表。</summary>
    /// <param name="gateway">任务页 RPC 网关。</param>
    public void AttachGateway(ICronGateway gateway)
    {
        if (ReferenceEquals(_gateway, gateway))
        {
            return;
        }

        DetachGateway();
        _gateway = gateway;
        OnPropertyChanged(nameof(IsLive));
        _ = RefreshAsync();
    }

    /// <summary>断开后摘掉网关（保留当前列表）。</summary>
    public void DetachGateway()
    {
        if (_gateway is null)
        {
            return;
        }

        _gateway = null;
        CancelLoads();
        OnPropertyChanged(nameof(IsLive));
    }

    /// <summary>连接状态变化后刷新 <see cref="IsLive"/>。</summary>
    public void NotifyConnectionChanged() => OnPropertyChanged(nameof(IsLive));

    /// <summary>切到任务页时刷新一次（任务可能刚被模型创建 / 停用）。</summary>
    public void EnsureLoaded()
    {
        OnPropertyChanged(nameof(IsLive));
        if (_gateway is not { IsConnected: true })
        {
            return;
        }

        _ = RefreshAsync();
    }

    /// <summary>重新拉任务列表，尽量停在原来选中的任务。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCts = cts;

        var selectedId = SelectedJob?.Id;
        IsLoading = true;
        ErrorText = "";
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var jobs = await gateway.ListAsync(includeDisabled: true, cts.Token).ConfigureAwait(true);
            Jobs.Clear();
            foreach (var job in jobs)
            {
                Jobs.Add(new CronJobItem(job));
            }

            var again = selectedId is null
                ? null
                : Jobs.FirstOrDefault(job => string.Equals(job.Id, selectedId, StringComparison.Ordinal));
            SelectedJob = again ?? Jobs.FirstOrDefault();
            if (SelectedJob is null)
            {
                Executions.Clear();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"任务列表载入失败：{Describe(ex)}";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
            }

            cts.Dispose();
            IsLoading = false;
            OnPropertyChanged(nameof(HasJobs));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HeaderSubtitle));
            OnPropertyChanged(nameof(HasError));
        }
    }

    /// <summary>刷新当前任务的执行记录。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    [RelayCommand]
    public async Task RefreshDetailAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedJob is { } job)
        {
            await LoadDetailAsync(job, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>启用 / 停用当前任务。</summary>
    [RelayCommand]
    private async Task ToggleEnabledAsync()
    {
        if (SelectedJob is not { } job || _gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            await gateway.SetEnabledAsync(job.Id, !job.Enabled).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"启停失败：{Describe(ex)}";
            OnPropertyChanged(nameof(HasError));
        }
    }

    /// <summary>点「删除任务」：先进入确认态。</summary>
    [RelayCommand]
    private void RequestDelete() => IsConfirmingDelete = SelectedJob is not null;

    /// <summary>取消删除。</summary>
    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    /// <summary>确认删除（连同执行台账）。</summary>
    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        IsConfirmingDelete = false;
        if (SelectedJob is not { } job || _gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            await gateway.RemoveAsync(job.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"删除失败：{Describe(ex)}";
            OnPropertyChanged(nameof(HasError));
        }
    }

    partial void OnSelectedJobChanged(CronJobItem? value)
    {
        IsConfirmingDelete = false;
        OnPropertyChanged(nameof(HasSelection));
        _ = LoadDetailAsync(value);
    }

    private async Task LoadDetailAsync(CronJobItem? job, CancellationToken cancellationToken = default)
    {
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _detailCts = null;

        if (job is null || _gateway is not { IsConnected: true } gateway)
        {
            Executions.Clear();
            OnPropertyChanged(nameof(HasExecutions));
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _detailCts = cts;
        try
        {
            var detail = await gateway.GetAsync(job.Id, 10, cts.Token).ConfigureAwait(true);
            if (!string.Equals(SelectedJob?.Id, job.Id, StringComparison.Ordinal))
            {
                return;
            }

            Executions.Clear();
            foreach (var execution in detail.Executions)
            {
                Executions.Add(new CronExecutionItem(execution));
            }

            OnPropertyChanged(nameof(HasExecutions));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"执行记录载入失败：{Describe(ex)}";
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            if (ReferenceEquals(_detailCts, cts))
            {
                _detailCts = null;
            }

            cts.Dispose();
        }
    }

    private void CancelLoads()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _detailCts = null;
    }

    private static string Describe(Exception ex) => ex switch
    {
        RpcClientException rpc => rpc.Message,
        _ => ex.Message,
    };
}
