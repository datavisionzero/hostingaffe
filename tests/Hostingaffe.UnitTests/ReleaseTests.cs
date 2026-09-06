using Hostingaffe.Domain.Releases;

namespace Hostingaffe.UnitTests;

public sealed class ReleaseTests
{
    [Fact]
    public void Publishing_names_and_freezes_the_open_release()
    {
        var now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var by = Guid.NewGuid();
        var release = Release.Open(Guid.NewGuid(), now.AddHours(-1));

        release.Publish(" v1.0.0 ", "Notes.", by, now);

        Assert.Equal("v1.0.0", release.Name);
        Assert.Equal(ReleaseStatus.Published, release.Status);
        Assert.Equal("Notes.", release.Description);
        Assert.Equal(now, release.PublishedAt);
        Assert.Equal(by, release.PublishedBy);
        Assert.Throws<InvalidOperationException>(() => release.Publish("v2", null, by, now));
    }

    // The name is a fumble to correct, not a record to rewrite; which release
    // may be corrected is the act's business, and the rest is here.
    [Fact]
    public void A_publication_is_renamed_and_taken_back()
    {
        var now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var by = Guid.NewGuid();
        var release = Release.Open(Guid.NewGuid(), now.AddHours(-1));
        Assert.Throws<InvalidOperationException>(() => release.Rename("v1", now));
        Assert.Throws<InvalidOperationException>(() => release.Retract(now));

        release.Publish("v1.0.O", "Notes.", by, now);
        release.Rename(" v1.0.0 ", now.AddMinutes(1));
        Assert.Equal("v1.0.0", release.Name);
        Assert.Equal(ReleaseStatus.Published, release.Status);
        Assert.Throws<ArgumentException>(() => release.Rename("unreleased", now));

        release.Retract(now.AddMinutes(2));
        Assert.Equal(ReleaseStatus.Open, release.Status);
        Assert.Null(release.Name);
        Assert.Null(release.PublishedAt);
        Assert.Null(release.PublishedBy);
        // The notes are the release's own and survive the correction.
        Assert.Equal("Notes.", release.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unreleased")]
    [InlineData("NONE")]
    [InlineData("two\nlines")]
    public void Invalid_and_reserved_names_are_refused(string name) =>
        Assert.Throws<ArgumentException>(() => Release.NormalizeName(name));
}
