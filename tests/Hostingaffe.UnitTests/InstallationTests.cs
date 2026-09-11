using Hostingaffe.Domain;
using Hostingaffe.Domain.Installations;

using Environment = Hostingaffe.Domain.Installations.Environment;

namespace Hostingaffe.UnitTests;

/// <summary>
/// What an installation holds itself to without asking the database: its
/// closed sets, its three lists, the path it lives at, and the rule that a
/// secret is named and never given.
/// </summary>
public sealed class InstallationTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly Guid Machine = Guid.CreateVersion7();

    private static readonly Guid Software = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static Installation An(string key = "logaffe-prod") =>
        Installation.Create(key, null, Machine, Software, Environment.Production, Role.Application, Actor, Now);

    [Fact]
    public void An_installation_without_a_name_is_called_by_its_key()
    {
        var installation = An();

        Assert.Equal("logaffe-prod", installation.Key);
        Assert.Equal("logaffe-prod", installation.Name);
    }

    [Fact]
    public void The_decisions_start_where_nothing_has_been_decided()
    {
        var installation = An();

        Assert.Equal(Status.Active, installation.Status);
        Assert.Equal(Backup.None, installation.Backup);
        Assert.Equal(Monitoring.None, installation.Monitoring);
        Assert.Equal(Logging.Local, installation.Logging);
        Assert.Empty(installation.Urls);
        Assert.Empty(installation.Ports);
        Assert.Empty(installation.Secrets);
    }

    [Fact]
    public void There_is_no_version_to_set()
    {
        // The type says it: nothing here carries a version, because the
        // deployments do (VISION 7).
        Assert.Null(typeof(Installation).GetProperty("Version"));
        Assert.Null(typeof(InstallationEdit).GetProperty("Version"));
        Assert.Null(typeof(InstallationEdit).GetProperty("DependsOn"));
    }

    [Fact]
    public void What_changed_is_what_the_installation_reports()
    {
        var installation = An();

        var changes = installation.Apply(
            new InstallationEdit
            {
                Environment = Environment.Staging,
                Backup = Backup.Active,
                Path = "/srv/logaffe",
            },
            Actor,
            Now);

        Assert.Equal(
            [("path", null, "/srv/logaffe"), ("environment", "production", "staging"), ("backup", "none", "active")],
            changes.Select(change => (change.Field, change.OldValue, change.NewValue)));

        var moved = installation.UpdatedAt;
        Assert.Empty(installation.Apply(new InstallationEdit(), Actor, Now.AddHours(1)));
        Assert.Equal(moved, installation.UpdatedAt);
    }

    [Fact]
    public void A_list_is_replaced_whole_and_reads_as_its_entries()
    {
        var installation = An();

        var changes = installation.Apply(
            new InstallationEdit
            {
                Ports = [Port.Of(443, Protocol.Tcp, Scope.Public), Port.Of(5432, Protocol.Tcp, Scope.Private)],
                Urls = ["https://logs.example.test"],
            },
            Actor,
            Now);

        var ports = changes.Single(change => change.Field == "ports");
        Assert.Null(ports.OldValue);
        Assert.Equal("443/tcp:public, 5432/tcp:private", ports.NewValue);

        // Emptying it is a change to nothing at all, not to an empty-looking string.
        var cleared = installation.Apply(new InstallationEdit { Ports = [], Urls = [] }, Actor, Now);
        Assert.Null(cleared.Single(change => change.Field == "ports").NewValue);
        Assert.Empty(installation.Ports);
    }

    [Fact]
    public void The_same_port_twice_is_refused_whatever_its_scope_says()
    {
        var installation = An();

        var refusal = Assert.Throws<ArgumentException>(() => installation.Apply(
            new InstallationEdit
            {
                Ports = [Port.Of(443, Protocol.Tcp, Scope.Public), Port.Of(443, Protocol.Tcp, Scope.Private)],
            },
            Actor,
            Now));

        Assert.Equal("ports", refusal.ParamName);
    }

    [Fact]
    public void A_port_is_a_number_a_transport_and_a_reach()
    {
        Assert.Equal("443/tcp:public", Port.Of(443, Protocol.Tcp, Scope.Public).ToString());
        Assert.Throws<ArgumentException>(() => Port.Of(0, Protocol.Tcp, Scope.Public));
        Assert.Throws<ArgumentException>(() => Port.Of(65536, Protocol.Udp, Scope.Internal));
    }

    [Theory]
    [InlineData("/srv/logaffe")]
    [InlineData("/opt/stacks/logaffe")]
    [InlineData("/")]
    public void A_path_is_where_it_lives_on_the_machine(string path) =>
        Assert.Equal(path, Installation.NormalizePath(path));

    [Theory]
    [InlineData("srv/logaffe")]
    [InlineData("/srv/../etc")]
    public void A_path_that_is_relative_or_climbs_is_refused(string path) =>
        Assert.Throws<ArgumentException>(() => Installation.NormalizePath(path));

    [Fact]
    public void A_trailing_slash_is_what_a_person_types_and_means_nothing() =>
        Assert.Equal("/srv/logaffe", Installation.NormalizePath("/srv/logaffe/"));

    [Fact]
    public void Data_is_the_second_directory_and_holds_the_same_shape_as_the_first()
    {
        Assert.Equal("/srv/services/logaffe", Installation.NormalizeData("/srv/services/logaffe/"));
        Assert.Throws<ArgumentException>(() => Installation.NormalizeData("srv/services/logaffe"));
        Assert.Throws<ArgumentException>(() => Installation.NormalizeData("/srv/../etc"));
    }

    [Fact]
    public void An_installation_carries_two_directories_and_nothing_holds_them_apart()
    {
        var installation = An();

        var changes = installation.Apply(
            new InstallationEdit { Path = "/opt/compose/logaffe", Data = "/srv/services/logaffe" },
            Actor,
            Now);

        Assert.Equal(
            [("path", null, "/opt/compose/logaffe"), ("data", null, "/srv/services/logaffe")],
            changes.Select(change => (change.Field, change.OldValue, change.NewValue)));

        // A host that keeps configuration and state in one directory says the
        // same thing twice, and that is an answer rather than a contradiction.
        var together = An();
        Assert.Equal(
            2,
            together.Apply(
                new InstallationEdit { Path = "/srv/logaffe", Data = "/srv/logaffe" }, Actor, Now).Count);
        Assert.Equal(together.Path, together.Data);

        // The empty string clears it, like every other text field.
        Assert.Equal("data", installation.Apply(new InstallationEdit { Data = "" }, Actor, Now).Single().Field);
        Assert.Null(installation.Data);
    }

    [Theory]
    [InlineData("LOGAFFE_DB_PASSWORD")]
    [InlineData("acme/cloudflare-token")]
    public void A_secret_is_named(string name) => Assert.Equal(name, Secret.Of(name, null).Name);

    [Theory]
    [InlineData("LOGAFFE_DB_PASSWORD=hunter2")]
    [InlineData("a secret")]
    [InlineData("")]
    public void A_secret_is_never_given(string name) =>
        Assert.Throws<ArgumentException>(() => Secret.Of(name, null));

    [Fact]
    public void A_secret_says_which_file_it_lies_in()
    {
        var secret = Secret.Of("LOGAFFE_DB_PASSWORD", "/opt/compose/logaffe/.env.runtime/");

        Assert.Equal("/opt/compose/logaffe/.env.runtime", secret.Path);
        Assert.Equal("LOGAFFE_DB_PASSWORD@/opt/compose/logaffe/.env.runtime", secret.ToString());

        // Nobody has decided where it goes yet, and that is a state the record
        // has to be able to hold: the name alone is what it was before.
        Assert.Null(Secret.Of("LOGAFFE_DB_PASSWORD", "  ").Path);
        Assert.Equal("LOGAFFE_DB_PASSWORD", Secret.Of("LOGAFFE_DB_PASSWORD", null).ToString());

        Assert.Throws<ArgumentException>(() => Secret.Of("LOGAFFE_DB_PASSWORD", "compose/logaffe/.env"));
        Assert.Throws<ArgumentException>(() => Secret.Of("LOGAFFE_DB_PASSWORD", "/opt/../etc/shadow"));
    }

    [Fact]
    public void A_secret_is_the_same_secret_by_its_name()
    {
        var installation = An();

        var refusal = Assert.Throws<ArgumentException>(() => installation.Apply(
            new InstallationEdit
            {
                Secrets =
                [
                    Secret.Of("LOGAFFE_DB_PASSWORD", "/opt/compose/logaffe/.env.runtime"),
                    Secret.Of("LOGAFFE_DB_PASSWORD", "/srv/services/logaffe/.env"),
                ],
            },
            Actor,
            Now));

        Assert.Equal("secrets", refusal.ParamName);
    }

    [Fact]
    public void The_history_reads_a_secret_the_way_a_person_writes_one()
    {
        var installation = An();

        var change = installation.Apply(
            new InstallationEdit
            {
                Secrets =
                [
                    Secret.Of("LOGAFFE_DB_PASSWORD", "/opt/compose/logaffe/.env.runtime"),
                    Secret.Of("SMTP_PASSWORD", null),
                ],
            },
            Actor,
            Now).Single();

        Assert.Equal("secrets", change.Field);
        Assert.Null(change.OldValue);
        Assert.Equal("LOGAFFE_DB_PASSWORD@/opt/compose/logaffe/.env.runtime, SMTP_PASSWORD", change.NewValue);

        // Moving one is a change to the list, because the list is what the
        // field is: there is no address for an entry to patch.
        var moved = installation.Apply(
            new InstallationEdit
            {
                Secrets =
                [
                    Secret.Of("LOGAFFE_DB_PASSWORD", "/srv/services/logaffe/.env"),
                    Secret.Of("SMTP_PASSWORD", null),
                ],
            },
            Actor,
            Now).Single();

        Assert.Equal("LOGAFFE_DB_PASSWORD@/opt/compose/logaffe/.env.runtime, SMTP_PASSWORD", moved.OldValue);
        Assert.Equal("LOGAFFE_DB_PASSWORD@/srv/services/logaffe/.env, SMTP_PASSWORD", moved.NewValue);
    }

    [Fact]
    public void A_url_in_the_list_is_a_url()
    {
        var refusal = Assert.Throws<ArgumentException>(() =>
            An().Apply(new InstallationEdit { Urls = ["logs.example.test"] }, Actor, Now));

        Assert.Equal("urls", refusal.ParamName);
    }

    [Fact]
    public void The_closed_sets_are_spelled_the_way_the_contract_spells_them()
    {
        Assert.Equal(["production", "staging", "development"], Enum.GetValues<Environment>().Select(Spelling.Of));
        Assert.Equal(["application", "platform"], Enum.GetValues<Role>().Select(Spelling.Of));
        Assert.Equal(["planned", "active", "retired"], Enum.GetValues<Status>().Select(Spelling.Of));

        // Two of the three decisions carry the same middle value, and it means
        // the same thing in both: decided on, not there yet.
        Assert.Equal(["none", "planned", "active"], Enum.GetValues<Backup>().Select(Spelling.Of));
        Assert.Equal(["none", "planned", "external"], Enum.GetValues<Monitoring>().Select(Spelling.Of));
        Assert.Equal(["local", "central"], Enum.GetValues<Logging>().Select(Spelling.Of));
    }
}
