using Shoyee.Notify.Email;
using Xunit;

namespace Shoyee.Notify.Email.Tests;

public sealed class SmtpEmailPusherTests
{
    [Fact]
    public async Task PushAsync_MissingHost_ThrowsActionableError()
    {
        var pusher = new SmtpEmailPusher(new EmailOptions(To: ["me@example.com"]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pusher.PushAsync("t", "b"));

        Assert.Contains("host", ex.Message);
    }

    [Fact]
    public async Task PushAsync_MissingRecipients_ThrowsActionableError()
    {
        var pusher = new SmtpEmailPusher(new EmailOptions(Host: "smtp.example.com"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pusher.PushAsync("t", "b"));

        Assert.Contains("to", ex.Message);
    }

    [Fact]
    public async Task PushAsync_MissingCredentials_ThrowsEnvironmentName()
    {
        var pusher = new SmtpEmailPusher(new EmailOptions(
            Host: "smtp.example.com",
            To: ["me@example.com"],
            UsernameEnv: "SHOYEE_TEST_MISSING_USER",
            PasswordEnv: "SHOYEE_TEST_MISSING_PASSWORD"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pusher.PushAsync("t", "b"));

        Assert.Contains("SHOYEE_TEST_MISSING_USER", ex.Message);
    }

    [Fact]
    public void Defaults_UseStartTlsJointAndEnvNames()
    {
        var options = new EmailOptions();

        Assert.Equal(587, options.Port);
        Assert.True(options.EnableSsl);
        Assert.Equal("SHOYEE_SMTP_USER", options.UsernameEnv);
        Assert.Equal("SHOYEE_SMTP_PASSWORD", options.PasswordEnv);
        Assert.Equal("[Shoyee] ", options.SubjectPrefix);
    }

    [Fact]
    public void NotifyOptions_BindsEmailProviderSection()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"host":"smtp.qq.com","port":587,"to":["me@example.com","other@example.com"]}""");
        var notify = new Shoyee.Contracts.Options.NotifyOptions(
            "email",
            new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["email"] = document.RootElement.Clone(),
            });

        var options = notify.GetProviderOptions<EmailOptions>("email");

        Assert.NotNull(options);
        Assert.Equal("smtp.qq.com", options.Host);
        Assert.Equal(["me@example.com", "other@example.com"], options.To);
    }
}
