using Hostingaffe.Application.Ports;

namespace Hostingaffe.UnitTests;

/// <summary>The three variables of ADR 0008, and what they refuse.</summary>
public sealed class LogSettingsTests
{
    [Fact]
    public void Nothing_set_is_the_console_and_a_file_at_information()
    {
        var settings = LogSettings.FromVariables(null, null, null);

        Assert.False(settings.ShipsToLogaffe);
        Assert.Equal("Information", settings.Level);
    }

    [Fact]
    public void Endpoint_and_token_together_ship_to_logaffe()
    {
        var settings = LogSettings.FromVariables("https://logs.example.org/", " ha-ingest-token ", "warning");

        Assert.True(settings.ShipsToLogaffe);
        Assert.Equal(new Uri("https://logs.example.org/"), settings.Endpoint);
        Assert.Equal("ha-ingest-token", settings.Token);
        Assert.Equal("Warning", settings.Level);
    }

    [Theory]
    [InlineData("https://logs.example.org", null, "HOSTINGAFFE_LOG_TOKEN")]
    [InlineData(null, "token", "HOSTINGAFFE_LOG_ENDPOINT")]
    [InlineData("logs.example.org", "token", "HOSTINGAFFE_LOG_ENDPOINT")]
    [InlineData("ftp://logs.example.org", "token", "HOSTINGAFFE_LOG_ENDPOINT")]
    [InlineData(null, null, "HOSTINGAFFE_LOG_LEVEL")]
    public void One_without_the_other_a_bad_address_or_an_unknown_level_is_refused(string? endpoint, string? token, string variable)
    {
        var refusal = Assert.Throws<ArgumentException>(() =>
            LogSettings.FromVariables(endpoint, token, variable == "HOSTINGAFFE_LOG_LEVEL" ? "loud" : null));

        Assert.Equal(variable, refusal.ParamName);
    }
}
