namespace Shoyee.Notify.Email;

/// <summary>
/// 邮件通知配置（SMTP）。密钥不写进配置文件：只写环境变量名，由启动方（launchd/终端）提供。
/// 常用组合：QQ/163/Gmail/iCloud 用 587 + STARTTLS（<see cref="EnableSsl"/>=true）。
/// 注意 <see cref="System.Net.Mail.SmtpClient"/> 不支持 465 的隐式 SSL，465 端口请改用 587。
/// </summary>
/// <param name="Host">SMTP 服务器（如 <c>smtp.qq.com</c>）。</param>
/// <param name="Port">端口（默认 587）。</param>
/// <param name="EnableSsl">是否 STARTTLS（默认 true）。</param>
/// <param name="UsernameEnv">用户名所在环境变量名。</param>
/// <param name="PasswordEnv">密码/授权码所在环境变量名。</param>
/// <param name="From">发件人（空 = 用 SMTP 用户名）。</param>
/// <param name="To">收件人列表（必填）。</param>
/// <param name="SubjectPrefix">标题前缀（默认 <c>[Shoyee] </c>）。</param>
/// <param name="TimeoutSeconds">SMTP 超时。</param>
public sealed record EmailOptions(
    string Host = "",
    int Port = 587,
    bool EnableSsl = true,
    string UsernameEnv = "SHOYEE_SMTP_USER",
    string PasswordEnv = "SHOYEE_SMTP_PASSWORD",
    string From = "",
    IReadOnlyList<string>? To = null,
    string SubjectPrefix = "[Shoyee] ",
    int TimeoutSeconds = 20);
