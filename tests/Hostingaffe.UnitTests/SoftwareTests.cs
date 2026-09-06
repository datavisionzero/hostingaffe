using Hostingaffe.Domain;

namespace Hostingaffe.UnitTests;

/// <summary>
/// What a software holds itself to without asking the database: its name, its
/// two URLs, and the image name that is a name and not a version.
/// </summary>
public sealed class SoftwareTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static Software A(string key = "caddy") => Software.Create(key, null, Actor, Now);

    [Fact]
    public void A_software_without_a_name_is_called_by_its_key()
    {
        var software = A();

        Assert.Equal("caddy", software.Key);
        Assert.Equal("caddy", software.Name);
    }

    [Fact]
    public void The_key_is_the_shape_of_a_key() =>
        Assert.Throws<ArgumentException>(() => Software.Create("Caddy Two", null, Actor, Now));

    [Fact]
    public void What_changed_is_what_the_software_reports()
    {
        var software = A();

        var changes = software.Apply(
            new SoftwareEdit
            {
                Name = "Caddy",
                Homepage = "https://caddyserver.com",
                Image = "caddy",
            },
            Actor,
            Now);

        Assert.Equal(
            [("name", "caddy", "Caddy"), ("homepage", null, "https://caddyserver.com"), ("image", null, "caddy")],
            changes.Select(change => (change.Field, change.OldValue, change.NewValue)));

        // Nothing given, nothing changed — and the version does not move.
        var moved = software.UpdatedAt;
        Assert.Empty(software.Apply(new SoftwareEdit(), Actor, Now.AddHours(1)));
        Assert.Equal(moved, software.UpdatedAt);
    }

    [Fact]
    public void The_empty_string_clears_a_text_field_and_the_name_falls_back_to_the_key()
    {
        var software = A();
        software.Apply(new SoftwareEdit { Name = "Caddy", Repository = "https://github.com/caddyserver/caddy" }, Actor, Now);

        var changes = software.Apply(new SoftwareEdit { Name = string.Empty, Repository = string.Empty }, Actor, Now);

        Assert.Equal("caddy", software.Name);
        Assert.Null(software.Repository);
        Assert.Contains(changes, change => change.Field == "repository" && change.NewValue is null);
    }

    [Fact]
    public void A_description_records_that_it_changed_and_not_how()
    {
        var software = A();

        var changes = software.Apply(new SoftwareEdit { Description = "The reverse proxy." }, Actor, Now);

        var description = Assert.Single(changes);
        Assert.Equal("description", description.Field);
        Assert.Null(description.OldValue);
        Assert.Null(description.NewValue);
    }

    [Theory]
    [InlineData("caddy")]
    [InlineData("ghcr.io/datavisionzero/logaffe")]
    [InlineData("docker.io/library/postgres")]
    [InlineData("localhost:5000/private/thing")]
    [InlineData("uptime-kuma")]
    public void An_image_is_a_name(string image) => Assert.Equal(image, Software.NormalizeImage(image));

    [Theory]
    [InlineData("caddy:2")]
    [InlineData("ghcr.io/datavisionzero/logaffe:1.4.0")]
    [InlineData("postgres@sha256:0123456789abcdef")]
    [InlineData("Caddy")]
    [InlineData("caddy/")]
    public void An_image_with_a_tag_a_digest_or_a_capital_is_refused(string image) =>
        Assert.Throws<ArgumentException>(() => Software.NormalizeImage(image));

    [Fact]
    public void The_tag_belongs_to_the_deployment_and_the_refusal_says_so() =>
        Assert.Contains(
            "the tag belongs to the deployment",
            Assert.Throws<ArgumentException>(() => Software.NormalizeImage("caddy:2")).Message,
            StringComparison.Ordinal);

    [Theory]
    [InlineData("https://caddyserver.com")]
    [InlineData("http://example.test/path")]
    public void A_url_is_an_absolute_http_address(string url) => Assert.Equal(url, Software.Url(url, "homepage"));

    [Theory]
    [InlineData("caddyserver.com")]
    [InlineData("ftp://example.test")]
    [InlineData("/relative")]
    [InlineData("https://")]
    public void Anything_else_is_not_a_url(string url) =>
        Assert.Throws<ArgumentException>(() => Software.Url(url, "homepage"));
}
