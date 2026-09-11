using Hostingaffe.Domain;
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

    private static readonly Anchor Owner = new(AnchorKind.Installation, Guid.CreateVersion7(), "logaffe-prod");

    private static readonly Anchor Machine = new(AnchorKind.Machine, Guid.CreateVersion7(), "ex44");

    private static File A(string path = "compose.override.yml", string content = "services:") =>
        File.Create(Owner, path, directory: null, content, executable: false, Actor, Now);

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

        file.Write("services: two", null, null, Actor, Now.AddHours(1));
        file.Write("services: three", null, null, Actor, Now.AddHours(2));

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

        Assert.Empty(file.Write("services:", executable: false, directory: null, Actor, Now.AddHours(1)));
        Assert.Equal(1, file.Revision);

        // The mode bit alone is a change, and it is one the history names.
        var changes = file.Write(null, executable: true, directory: null, Actor, Now.AddHours(2));
        Assert.Equal(["revision", "executable"], changes.Select(change => change.Field));
        Assert.Equal(2, file.Revision);
        Assert.Equal("services:", file.Content);
    }

    [Fact]
    public void The_mode_bit_belongs_to_the_revision_so_an_old_one_comes_back_whole()
    {
        var file = File.Create(Owner, "bin/deploy", directory: null, "#!/bin/sh", executable: true, Actor, Now);
        file.Write("#!/bin/sh\nset -e", executable: false, directory: null, Actor, Now.AddHours(1));

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
    public void The_size_is_the_newest_revision_in_bytes_of_utf_8()
    {
        var file = A(content: "services:");
        Assert.Equal(9, file.Size);

        // Bytes, not characters — the same count the megabyte is measured with,
        // so that a size somebody reads answers "does this still fit?".
        file.Write("ä😀", null, null, Actor, Now.AddHours(1));
        Assert.Equal(6, file.Size);

        // It follows the newest revision, like everything else a file says now.
        Assert.Equal(9, System.Text.Encoding.UTF8.GetByteCount(file.At(1)!.Content));
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
    public void A_machines_file_says_which_directory_on_the_machine_it_lies_in()
    {
        var unit = File.Create(Machine, "caddy-host-backup.service", "/etc/systemd/system/", "[Unit]", false, Actor, Now);

        // One trailing slash is what a person types and means nothing.
        Assert.Equal("/etc/systemd/system", unit.Directory);
    }

    [Fact]
    public void A_machines_file_without_a_directory_is_refused()
    {
        foreach (var directory in new string?[] { null, "", "   ", "etc/systemd/system", "/etc/../root", "/etc/./x" })
        {
            Assert.Throws<ArgumentException>(
                () => File.Create(Machine, "caddy-host-backup.service", directory, "[Unit]", false, Actor, Now));
        }
    }

    [Fact]
    public void An_installations_file_carries_no_directory_of_its_own()
    {
        Assert.Null(A().Directory);

        // The installation's own path is the one directory all of its files lie
        // under, and a second answer here would be the one that drifts.
        var refused = Assert.Throws<ArgumentException>(
            () => File.Create(Owner, "compose.override.yml", "/srv/logaffe", "services:", false, Actor, Now));

        Assert.Equal("directory", refused.ParamName);
    }

    [Fact]
    public void Moving_a_file_on_the_machine_makes_no_revision()
    {
        var unit = File.Create(Machine, "logaffe.service", "/etc/systemd/system", "[Unit]", false, Actor, Now);

        var changes = unit.Write(null, null, "/usr/local/lib/systemd/system", Actor, Now.AddHours(1));

        // Where a file lies is not what it says, so the history names the move
        // and the revision stays where it was.
        Assert.Equal(["directory"], changes.Select(change => change.Field));
        Assert.Equal("/etc/systemd/system", changes[0].OldValue);
        Assert.Equal("/usr/local/lib/systemd/system", changes[0].NewValue);
        Assert.Equal("/usr/local/lib/systemd/system", unit.Directory);
        Assert.Equal(1, unit.Revision);

        // And writing the same one again changes nothing at all.
        Assert.Empty(unit.Write(null, null, "/usr/local/lib/systemd/system", Actor, Now.AddHours(2)));
    }

    [Fact]
    public void A_machines_file_cannot_be_left_without_a_directory_by_a_write()
    {
        var unit = File.Create(Machine, "logaffe.service", "/etc/systemd/system", "[Unit]", false, Actor, Now);

        Assert.Throws<ArgumentException>(() => unit.Write(null, null, "", Actor, Now.AddHours(1)));
        Assert.Equal("/etc/systemd/system", unit.Directory);
    }

    [Fact]
    public void An_owner_is_a_kind_and_a_key()
    {
        Assert.Equal("installation logaffe-prod", Owner.ToString());
        Assert.Equal("machine ex44", new Anchor(AnchorKind.Machine, Guid.CreateVersion7(), "ex44").ToString());
    }
}
