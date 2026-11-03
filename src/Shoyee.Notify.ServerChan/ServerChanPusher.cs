using System.Net;
using System.Text.Json;
using Shoyee.Contracts.Abstractions;

namespace Shoyee.Notify.ServerChan;

/// <summary>
/// Server酱³ 通知实现：直接 <c>POST https://{uid}.push.ft07.com/send/{sendkey}.send</c>，
/// 凭据读自推送脚本的 <c>config.sh</c>（Shoyee 不写、不落库）。
/// </summary>
public sealed class ServerChanPusher : INotificationPusher, IDisposable
{
    private const string UidKey = "SCT_UID";
    private const string SendKeyKey = "SCT_SENDKEY";

    private readonly ServerChanOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>创建推送器。</summary>
    /// <param name="options">Server酱自己的配置节。</param>
    /// <param name="http">测试可注入的 HttpClient。</param>
    public ServerChanPusher(ServerChanOptions options, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = http is null;
    }

    /// <summary>供应商 key。</summary>
    public const string ProviderKey = "serverchan";

    /// <inheritdoc />
    public async Task PushAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        var credentials = ShellConfig.Read(_options.ConfigFile);
        if (!credentials.TryGetValue(UidKey, out var uid) || string.IsNullOrWhiteSpace(uid))
        {
            throw new InvalidOperationException(
                $"Server酱凭据缺失：{ShellConfig.ExpandHome(_options.ConfigFile)} 里没有 {UidKey}。");
        }

        if (!credentials.TryGetValue(SendKeyKey, out var sendKey) || string.IsNullOrWhiteSpace(sendKey))
        {
            throw new InvalidOperationException(
                $"Server酱凭据缺失：{ShellConfig.ExpandHome(_options.ConfigFile)} 里没有 {SendKeyKey}。");
        }

        var endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
            ? $"https://{uid}.push.ft07.com/send/{sendKey}.send"
            : _options.Endpoint;

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["title"] = title,
            ["desp"] = body,
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        HttpResponseMessage response;
        string payload;
        try
        {
            response = await _http.PostAsync(new Uri(endpoint), content, timeoutCts.Token).ConfigureAwait(false);
            payload = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Server酱推送超时（{_options.TimeoutSeconds} 秒）。");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Server酱推送失败（HTTP {(int)response.StatusCode}）：{Snippet(payload)}");
            }
        }

        if (!IsSuccess(payload, out var message))
        {
            throw new InvalidOperationException($"Server酱推送失败：{message}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private static bool IsSuccess(string payload, out string message)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("code", out var code))
            {
                message = $"响应缺少 code：{Snippet(payload)}";
                return false;
            }

            var ok = code.ValueKind switch
            {
                JsonValueKind.Number => code.TryGetInt32(out var number) && number == 0,
                JsonValueKind.String => code.GetString() == "0",
                _ => false,
            };
            message = ok ? string.Empty : Snippet(payload);
            return ok;
        }
        catch (JsonException)
        {
            message = $"响应不是 JSON：{Snippet(payload)}";
            return false;
        }
    }

    private static string Snippet(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "…";
    }
}
