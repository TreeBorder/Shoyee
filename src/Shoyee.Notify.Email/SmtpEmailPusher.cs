using System.Net;
using System.Net.Mail;
using Shoyee.Contracts.Abstractions;

namespace Shoyee.Notify.Email;

/// <summary>
/// 邮件通知：SMTP 直连（<see cref="SmtpClient"/>），纯文本正文。
/// 通知是无人值守场景，不走 Mail.app 自动化（TCC 授权在 launchd 下不可靠）。
/// </summary>
public sealed class SmtpEmailPusher : INotificationPusher
{
    /// <summary>供应商 key。</summary>
    public const string ProviderKey = "email";

    private readonly EmailOptions _options;

    /// <summary>创建推送器。</summary>
    /// <param name="options">邮件自己的配置节。</param>
    public SmtpEmailPusher(EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public async Task PushAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            throw new InvalidOperationException("邮件通知未配置 SMTP 服务器（notify.providers.email.host）。");
        }

        if (_options.To is not { Count: > 0 } recipients)
        {
            throw new InvalidOperationException("邮件通知未配置收件人（notify.providers.email.to）。");
        }

        var username = RequireEnvironment(_options.UsernameEnv);
        var password = RequireEnvironment(_options.PasswordEnv);
        var from = string.IsNullOrWhiteSpace(_options.From) ? username : _options.From;

        using var message = new MailMessage
        {
            From = new MailAddress(from),
            Subject = BuildSubject(title),
            Body = body,
            IsBodyHtml = false,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var recipient in recipients)
        {
            message.To.Add(recipient);
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Timeout = (int)TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)).TotalMilliseconds,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
        };

        try
        {
            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"邮件发送超时（{_options.TimeoutSeconds} 秒）。");
        }
    }

    private string BuildSubject(string title)
    {
        var subject = (_options.SubjectPrefix + title).ReplaceLineEndings(" ").Trim();
        return subject.Length <= 200 ? subject : subject[..200];
    }

    private static string RequireEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"邮件通知缺少环境变量：{name}。");
        }

        return value;
    }
}
