using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;

namespace Hostingaffe.UnitTests;

public sealed class SmtpSettingsTests
{
    [Fact]
    public void No_smtp_variables_means_an_unconfigured_capability()
    {
        var settings = SmtpSettings.FromVariables(null, null, null, null, null, null, null, null, development: false);

        Assert.False(settings.Configured);
        Assert.Null(settings.Host);
    }

    [Fact]
    public void Public_url_is_available_for_browser_security_without_smtp()
    {
        var settings = SmtpSettings.FromVariables(
            null, null, null, null, null, null, null,
            "https://plan.example.test", development: false);

        Assert.False(settings.Configured);
        Assert.Equal(new Uri("https://plan.example.test"), settings.PublicUrl);
    }

    [Fact]
    public void A_complete_development_configuration_is_normalized()
    {
        var settings = SmtpSettings.FromVariables(
            " mail.test ", "1025", null, null, "NONE", "sender@example.test", null,
            "http://localhost:8080", development: true);

        Assert.True(settings.Configured);
        Assert.Equal("mail.test", settings.Host);
        Assert.Equal(SmtpSecurity.None, settings.Security);
        Assert.Equal("hostingaffe <sender@example.test>", settings.Sender);
    }

    [Theory]
    [InlineData("user", null)]
    [InlineData(null, "secret")]
    public void Credentials_are_a_pair(string? username, string? password)
    {
        Assert.Throws<ArgumentException>(() => SmtpSettings.FromVariables(
            "mail.test", null, username, password, null, "sender@example.test", null,
            "https://plan.example.test", development: false));
    }

    [Fact]
    public void Plain_smtp_is_refused_outside_development()
    {
        Assert.Throws<ArgumentException>(() => SmtpSettings.FromVariables(
            "mail.test", null, null, null, "none", "sender@example.test", null,
            "https://plan.example.test", development: false));
    }

    [Fact]
    public void Link_templates_have_text_and_html_bodies_without_unescaped_names()
    {
        var message = TransactionalEmailTemplates.Invitation(
            "person@example.test", "A <Person>", new Uri("https://plan.example.test/activate?secret=abc"));

        Assert.Contains("A <Person>", message.TextBody);
        Assert.Contains("A &lt;Person&gt;", message.HtmlBody);
        Assert.Contains("https://plan.example.test/activate?secret=abc", message.TextBody);
    }
}
