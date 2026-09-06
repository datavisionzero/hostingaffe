using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.Files;

using File = Hostingaffe.Domain.Files.File;

namespace Hostingaffe.UnitTests;

/// <summary>
/// The one place that says what "latest" means, and the one thing this epic
/// would fail on: everything derived is ordered by <c>at</c>, never by the
/// order of recording.
/// </summary>
public sealed class DerivedTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly Guid Installation = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static Deployment One(int number, string version, DateTimeOffset at) =>
        Deployment.Record(Installation, number, version, at, Actor, Now);

    [Fact]
    public void Nothing_recorded_is_no_version()
    {
        Assert.Null(Derived.Version([]));
        Assert.Null(Derived.Latest([]));
    }

    [Fact]
    public void The_version_is_the_latest_by_at()
    {
        var first = One(1, "1.0.0", Now.AddDays(-10));
        var second = One(2, "1.1.0", Now.AddDays(-1));

        Assert.Equal("1.1.0", Derived.Version([first, second]));
        Assert.Equal("1.1.0", Derived.Version([second, first]));
    }

    [Fact]
    public void A_backfilled_deployment_with_an_older_at_does_not_move_the_present()
    {
        var first = One(1, "1.0.0", Now.AddDays(-10));
        var second = One(2, "1.1.0", Now.AddDays(-1));

        // Recorded third, but it ran before both.
        var backfilled = One(3, "0.9.0", Now.AddDays(-20));

        Assert.Equal("1.1.0", Derived.Version([first, second, backfilled]));

        // And one backfilled with a newer at moves it, which is the same rule.
        var later = One(4, "1.2.0", Now);
        Assert.Equal("1.2.0", Derived.Version([first, second, backfilled, later]));
    }

    [Fact]
    public void Previous_uses_the_same_order_the_version_does()
    {
        var oldest = One(1, "1.0.0", Now.AddDays(-10));
        var newest = One(2, "1.1.0", Now.AddDays(-1));
        var backfilled = One(3, "0.9.0", Now.AddDays(-20));

        var all = new[] { oldest, newest, backfilled };

        Assert.Null(Derived.Previous(all, backfilled));
        Assert.Equal("0.9.0", Derived.Previous(all, oldest));
        Assert.Equal("1.0.0", Derived.Previous(all, newest));
    }

    [Fact]
    public void At_can_repeat_and_the_number_is_what_breaks_the_tie()
    {
        var same = Now.AddDays(-1);
        var first = One(1, "1.0.0", same);
        var second = One(2, "1.1.0", same);

        Assert.Equal("1.1.0", Derived.Version([second, first]));
        Assert.Equal("1.0.0", Derived.Previous([second, first], second));
    }

    [Fact]
    public void Files_are_the_revisions_that_were_current_at_that_moment()
    {
        var owner = new FileOwner(OwnerKind.Installation, Installation, "logaffe-prod");

        var compose = File.Create(owner, "compose.override.yml", "one", false, Actor, Now.AddDays(-10));
        compose.Write("two", null, Actor, Now.AddDays(-5));
        compose.Write("three", null, Actor, Now.AddDays(-1));

        var later = File.Create(owner, "bin/deploy", "#!/bin/sh", true, Actor, Now.AddDays(-2));

        var files = new[] { compose, later };

        // Before the first file was put: empty, which is the honest answer.
        Assert.Empty(Derived.FilesAt(files, Now.AddDays(-20)));

        Assert.Equal(
            [("compose.override.yml", 2)],
            Derived.FilesAt(files, Now.AddDays(-4)).Select(file => (file.Path, file.Revision)));

        Assert.Equal(
            [("bin/deploy", 1), ("compose.override.yml", 3)],
            Derived.FilesAt(files, Now).Select(file => (file.Path, file.Revision)));
    }
}
