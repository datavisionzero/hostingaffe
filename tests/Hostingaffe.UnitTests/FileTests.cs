using Hostingaffe.Domain.Files;

using File = Hostingaffe.Domain.Files.File;
using FilePath = Hostingaffe.Domain.Files.FilePath;

namespace Hostingaffe.UnitTests;

/// <summary>
/// What a file holds itself to without asking the database: the one list of
/// refused paths, the megabyte, and that every write is a revision and every
/// earlier content stays.
/// </summary>
public sealed class FileTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly FileOwner Owner = new(OwnerKind.Installation, Guid.CreateVersion7(), "logaffe-prod");

    private static File A(string path = "compose.override.yml", string content = "services:") =>
        File.Create(Owner, path, content, executable: false, Actor, Now);

    [Fact]
    public void A_new_file_is_its_first_revision()
    {
        var file = A();

        Assert.Equal(1, file.Revision);
        Assert.Equal("services:", file.Content);
        Assert.Equal(Now, file.UpdatedAt);
        Assert.Single(file.Revisions);
    }

    [Fact]
    public void Every_write_is_a_revision_and_every_earlier_content_stays()
    {
        var file = A();

        file.Write("services: two", null, Actor, Now.AddHours(1));
        file.Write("services: three", null, Actor, Now.AddHours(2));

        Assert.Equal(3, file.Revision);
        Assert.Equal("services: three", file.Content);
        Assert.Equal("services:", file.At(1)!.Content);
        Assert.Equal("services: two", file.At(2)!.Content);
        Assert.Null(file.At(4));
    }

    [Fact]
    public void A_write_that_changes_nothing_makes_no_revision()
    {
        var file = A();

        Assert.Empty(file.Write("services:", executable: false, Actor, Now.AddHours(1)));
        Assert.Equal(1, file.Revision);

        // The mode bit alone is a change, and it is one the history names.
        var changes = file.Write(null, executable: true, Actor, Now.AddHours(2));
        Assert.Equal(["revision", "executable"], changes.Select(change => change.Field));
        Assert.Equal(2, file.Revision);
        Assert.Equal("services:", file.Content);
    }

    [Fact]
    public void The_mode_bit_belongs_to_the_revision_so_an_old_one_comes_back_whole()
    {
        var file = File.Create(Owner, "bin/deploy", "#!/bin/sh", executable: true, Actor, Now);
        file.Write("#!/bin/sh\nset -e", executable: false, Actor, Now.AddHours(1));

        Assert.False(file.Executable);
        Assert.True(file.At(1)!.Executable);
    }

    [Theory]
    [InlineData("compose.override.yml")]
    [InlineData("sites/logaffe.caddy")]
    [InlineData("bin/logaffe-stack")]
    [InlineData(".envrc")]
    [InlineData(".env.example")]
    public void A_path_is_relative_and_inside_the_owners_directory(string path) =>
        Assert.Equal(path, FilePath.Normalize(path));

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.production")]
    [InlineData("stacks/logaffe/.env")]
    [InlineData("secrets/token")]
    [InlineData("stacks/secrets/token")]
    [InlineData("/etc/systemd/system/logaffe.service")]
    [InlineData("../elsewhere")]
    [InlineData("bin/../../etc/passwd")]
    [InlineData("./here")]
    [InlineData("")]
    public void The_refused_paths_are_refused(string path) =>
        Assert.Throws<ArgumentException>(() => FilePath.Normalize(path));

    [Fact]
    public void The_refusal_says_which_rule_it_broke()
    {
        Assert.Contains(
            "secrets/",
            Assert.Throws<ArgumentException>(() => FilePath.Normalize("secrets/token")).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            ".env.example",
            Assert.Throws<ArgumentException>(() => FilePath.Normalize(".env.production")).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_content_over_a_megabyte_is_refused()
    {
        Assert.Equal(1024 * 1024, File.ContentMaxBytes);

        var whole = new string('x', File.ContentMaxBytes);
        Assert.Equal(whole, File.NormalizeContent(whole));

        Assert.Throws<ArgumentException>(() => File.NormalizeContent(whole + "x"));

        // Bytes, not characters: a multi-byte character counts for what it costs.
        Assert.Throws<ArgumentException>(() => File.NormalizeContent(new string('ä', File.ContentMaxBytes / 2 + 1)));
    }

    [Fact]
    public void What_is_not_text_is_refused_rather_than_replaced()
    {
        Assert.Throws<ArgumentException>(() => File.NormalizeContent("one\0two"));
        Assert.Throws<ArgumentException>(() => File.NormalizeContent("one\ud800two"));

        // A whole pair is a character, and characters are welcome.
        Assert.Equal("one😀two", File.NormalizeContent("one😀two"));
    }

    [Fact]
    public void An_owner_is_a_kind_and_a_key()
    {
        Assert.Equal("installation logaffe-prod", Owner.ToString());
        Assert.Equal("machine ex44", new FileOwner(OwnerKind.Machine, Guid.CreateVersion7(), "ex44").ToString());
    }
}
