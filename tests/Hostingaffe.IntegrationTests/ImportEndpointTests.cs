using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The bulk write (<c>docs/api.md</c>, Importing): a whole host in one
/// transaction, all or nothing.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ImportEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// A host with its software, two installations, files and a deployment
    /// each, out of one document.
    /// </summary>
    [Fact]
    public async Task A_whole_host_arrives_in_one_call()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var imported = await admin.PostAsJsonAsync(
            "/api/import?note=migrated%20from%20the%20old%20repository", AHost(), Ct);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);

        var made = await imported.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, made.GetProperty("machines").GetInt32());
        Assert.Equal(1, made.GetProperty("software").GetInt32());
        Assert.Equal(2, made.GetProperty("installations").GetInt32());
        Assert.Equal(2, made.GetProperty("deployments").GetInt32());
        Assert.Equal(3, made.GetProperty("files").GetInt32());
        Assert.Equal(1, made.GetProperty("pages").GetInt32());

        var machine = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        Assert.Equal("The big one", machine.GetProperty("name").GetString());
        Assert.Equal("fsn1-dc14", machine.GetProperty("location").GetString());

        var installation = await admin.GetFromJsonAsync<JsonElement>("/api/installations/app-1", Ct);
        Assert.Equal("ex44", installation.GetProperty("machine").GetString());
        Assert.Equal("logaffe", installation.GetProperty("software").GetString());
        Assert.Equal("1.4.0", installation.GetProperty("version").GetString());
        Assert.Equal("/opt/compose/app-1", installation.GetProperty("path").GetString());
        Assert.Equal("/srv/services/app-1", installation.GetProperty("data").GetString());
        Assert.Equal(443, installation.GetProperty("ports")[0].GetProperty("port").GetInt32());
        Assert.Equal("DB_PASSWORD", installation.GetProperty("secrets")[0].GetProperty("name").GetString());
        Assert.Equal(
            "/opt/compose/app-1/.env.runtime",
            installation.GetProperty("secrets")[0].GetProperty("path").GetString());

        var file = await admin.GetFromJsonAsync<JsonElement>(
            "/api/installations/app-1/files/compose.yml", Ct);
        Assert.Equal("services:\n  app:\n    image: logaffe:1.4.0\n", file.GetProperty("content").GetString());
        Assert.Equal(1, file.GetProperty("revision").GetInt32());

        // The machine's own file is under the machine, and the page hangs
        // where the document said it does.
        var caddy = await admin.GetFromJsonAsync<JsonElement>(
            "/api/machines/ex44/files/sites/app-1.caddy", Ct);
        Assert.Equal("app-1.example.test {\n  reverse_proxy app-1:8080\n}\n", caddy.GetProperty("content").GetString());

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/pages/backup-restore", Ct);
        Assert.Equal("runbook", page.GetProperty("kind").GetString());
        Assert.Equal("ex44", page.GetProperty("attached_to").GetProperty("key").GetString());

        // A deployment arrives as it was recorded, at the moment it happened.
        var deployments = await admin.GetFromJsonAsync<JsonElement>(
            "/api/installations/app-1/deployments", Ct);
        var deployment = Assert.Single(deployments.EnumerateArray());
        Assert.Equal("1.4.0", deployment.GetProperty("version").GetString());
        Assert.Equal("LOG-42", deployment.GetProperty("ticket").GetString());
        Assert.StartsWith("2026-09-05T12:00:00", deployment.GetProperty("at").GetString(), StringComparison.Ordinal);

        // The note it carried is beside every change it made.
        var history = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/history", Ct);
        Assert.Equal(
            "migrated from the old repository",
            history[0].GetProperty("note").GetString());
    }

    /// <summary>All or nothing: the last installation fails and nothing stands.</summary>
    [Fact]
    public async Task A_refusal_anywhere_leaves_nothing_standing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        // The software of the last installation is one nothing in the document
        // creates: the whole thing has to fail on it.
        var broken = AHost();
        var machines = (List<object>)broken["machines"]!;
        var installations = (List<object>)((Dictionary<string, object?>)machines[0])["installations"]!;
        installations[^1] = new Dictionary<string, object?>
        {
            ["key"] = "app-2",
            ["machine"] = "ex44",
            ["software"] = "never-installed",
            ["environment"] = "production",
            ["role"] = "application",
        };

        await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/import", broken, Ct),
            HttpStatusCode.BadRequest,
            "validation");

        // Not the machine, not the software, not the first installation, not
        // its files, not the page.
        foreach (var address in new[]
        {
            "/api/machines/ex44", "/api/software/logaffe", "/api/installations/app-1",
            "/api/installations/app-1/files/compose.yml", "/api/pages/backup-restore",
        })
        {
            using var missing = await admin.GetAsync(address, Ct);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        // And the keys are not spent: the whole thing can be sent again.
        using var again = await admin.PostAsJsonAsync("/api/import", AHost(), Ct);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    /// <summary>
    /// What an export writes is what the import reads: every field of an export
    /// is accepted, and the record it makes says the same thing the export did.
    /// What it does not say is the account of how the source got there — the
    /// history, the timestamps and who wrote them begin here
    /// (<c>docs/api.md</c>, Importing).
    /// </summary>
    [Fact]
    public async Task What_an_export_writes_is_what_an_import_reads()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        // A document with every field an export carries, the ones only the
        // instance writes included, exactly as `ha export` writes them.
        var document = new Dictionary<string, object?>
        {
            ["machines"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["key"] = "ex44",
                    ["name"] = "The big one",
                    ["hostname"] = "ex44",
                    ["kind"] = "dedicated",
                    ["host"] = null,
                    ["provider"] = "hetzner",
                    ["plan"] = "EX44",
                    ["location"] = "fsn1-dc14",
                    ["os"] = "Ubuntu 26.04 LTS",
                    ["arch"] = "amd64",
                    ["cpu"] = "Intel i5-13500",
                    ["memory"] = "64G",
                    ["disk"] = "2×512G NVMe ZFS mirror",
                    ["ipv4"] = "192.0.2.10",
                    ["ipv6"] = null,
                    ["private_ip"] = null,
                    ["ssh"] = "ex44",
                    ["status"] = "active",
                    ["measured_at"] = "2026-09-01T08:00:00Z",
                    ["description"] = "The box everything else sits on.",
                    ["created_by"] = new { id = Guid.Empty, kind = "user", name = "somebody" },
                    ["updated_by"] = new { id = Guid.Empty, kind = "user", name = "somebody" },
                    ["created_at"] = "2026-09-01T08:00:00Z",
                    ["updated_at"] = "2026-09-01T08:00:00Z",
                    ["history"] = new List<object>
                    {
                        new
                        {
                            at = "2026-09-01T08:00:00Z",
                            actor = new { id = Guid.Empty, kind = "user", name = "somebody" },
                            field = "status",
                            from = "planned",
                            to = "active",
                            note = "racked",
                        },
                    },
                    ["installations"] = new List<object>(),
                    ["files"] = new List<object>(),
                },
            },
        };

        using var imported = await admin.PostAsJsonAsync("/api/import", document, Ct);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);

        var machine = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        Assert.Equal("2×512G NVMe ZFS mirror", machine.GetProperty("disk").GetString());

        // What only the instance writes is the instance's, whatever the
        // document said: the caller is who imported it, at the moment they did.
        Assert.Equal("maintainer", machine.GetProperty("created_by").GetProperty("name").GetString());
        Assert.NotEqual("2026-09-01T08:00:00Z", machine.GetProperty("created_at").GetString());

        // And the history begins here rather than arriving with the document:
        // the record travels, the account of how it got there does not, so the
        // one entry is the creation this import made.
        var history = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/history", Ct);
        var entry = Assert.Single(history.EnumerateArray());
        Assert.Equal("created", entry.GetProperty("field").GetString());
        Assert.Equal("maintainer", entry.GetProperty("actor").GetProperty("name").GetString());
    }

    /// <summary>A field neither writable nor an export's is `unknown-field`, as everywhere.</summary>
    [Fact]
    public async Task A_field_the_document_invents_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        var problem = await Refusals.Problem(
            await admin.PostAsJsonAsync(
                "/api/import",
                new
                {
                    machines = new[] { new { key = "ex44", kind = "dedicated", rack = "B12" } },
                },
                Ct),
            HttpStatusCode.BadRequest,
            "unknown-field");

        Assert.Equal("rack", problem.GetProperty("field").GetString());
    }

    /// <summary>A vm runs on a machine that is further down the same document.</summary>
    [Fact]
    public async Task A_vm_finds_its_host_in_the_same_document()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var imported = await admin.PostAsJsonAsync(
            "/api/import",
            new
            {
                machines = new object[]
                {
                    new { key = "inner", kind = "vm", host = "outer" },
                    new { key = "outer", kind = "dedicated" },
                },
            },
            Ct);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);

        var vm = await admin.GetFromJsonAsync<JsonElement>("/api/machines/inner", Ct);
        Assert.Equal("outer", vm.GetProperty("host").GetString());
    }

    [Fact]
    public async Task An_empty_document_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        var problem = await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/import", new { }, Ct),
            HttpStatusCode.BadRequest,
            "validation");

        Assert.Equal("document", problem.GetProperty("errors").EnumerateObject().Single().Name);
    }

    [Fact]
    public async Task An_installation_that_says_another_machine_than_it_is_under_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Refusals.Problem(
            await admin.PostAsJsonAsync(
                "/api/import",
                new
                {
                    software = new[] { new { key = "logaffe" } },
                    machines = new object[]
                    {
                        new
                        {
                            key = "ex44",
                            kind = "dedicated",
                            installations = new object[]
                            {
                                new
                                {
                                    key = "app-1",
                                    machine = "somewhere-else",
                                    software = "logaffe",
                                    environment = "production",
                                    role = "application",
                                },
                            },
                        },
                    },
                },
                Ct),
            HttpStatusCode.BadRequest,
            "validation");
    }

    /// <summary>
    /// The cross-references a Markdown repository is full of become links to
    /// the pages they arrive as (ADR 0007).
    /// </summary>
    [Fact]
    public async Task A_relative_path_between_two_pages_becomes_a_link_to_the_page_it_arrives_as()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var imported = await admin.PostAsJsonAsync(
            "/api/import",
            new
            {
                pages = new object[]
                {
                    new
                    {
                        slug = "setup",
                        title = "Setting it up",
                        path = "docs/setup/README.md",
                        body = "Read [the decision](../decisions/2026-09-10-caddy.md) first, "
                            + "and [what is not here](../security/README.md) is not here.",
                    },
                    new
                    {
                        slug = "caddy-in-front",
                        title = "Caddy in front",
                        path = "docs/decisions/2026-09-10-caddy.md",
                        kind = "decision",
                        body = "Back to [the setup](./README.md)? No: [that one](../setup/README.md).",
                    },
                },
            },
            Ct);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);

        var made = await imported.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(2, made.GetProperty("pages").GetInt32());
        Assert.Equal(2, made.GetProperty("links").GetInt32());

        var setup = await admin.GetFromJsonAsync<JsonElement>("/api/pages/setup", Ct);
        Assert.Equal(
            "Read [the decision](page:caddy-in-front) first, and [what is not here](../security/README.md) is not here.",
            setup.GetProperty("body").GetString());

        // `./README.md` beside a decision is a page nothing in the document
        // arrives as, and it is left alone rather than guessed at.
        var decision = await admin.GetFromJsonAsync<JsonElement>("/api/pages/caddy-in-front", Ct);
        Assert.Equal(
            "Back to [the setup](./README.md)? No: [that one](page:setup).",
            decision.GetProperty("body").GetString());
    }

    /// <summary>
    /// Five directories with a `README.md` in each are ordinary in a repository
    /// and are one name here. The refusal says which two files collided.
    /// </summary>
    [Fact]
    public async Task Two_pages_that_want_one_slug_are_refused_by_name()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        var problem = await Refusals.Problem(
            await admin.PostAsJsonAsync(
                "/api/import",
                new
                {
                    pages = new object[]
                    {
                        new { slug = "readme", title = "Setup", path = "docs/setup/README.md" },
                        new { slug = "readme", title = "Operations", path = "docs/operations/README.md" },
                    },
                },
                Ct),
            HttpStatusCode.BadRequest,
            "validation");

        var said = problem.GetProperty("errors").GetProperty("slug")[0].GetString();
        Assert.Contains("docs/setup/README.md", said, StringComparison.Ordinal);
        Assert.Contains("docs/operations/README.md", said, StringComparison.Ordinal);
        Assert.Contains("readme", said, StringComparison.Ordinal);

        // Nothing stood: the first of the two is not there either.
        using var missing = await admin.GetAsync("/api/pages/readme", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>The document the tests above start from: one host, whole.</summary>
    private static Dictionary<string, object?> AHost() => new()
    {
        ["software"] = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["key"] = "logaffe",
                ["name"] = "logaffe",
                ["image"] = "ghcr.io/datavisionzero/logaffe",
                ["description"] = "The log everything writes into.",
            },
        },
        ["machines"] = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["key"] = "ex44",
                ["name"] = "The big one",
                ["kind"] = "dedicated",
                ["provider"] = "hetzner",
                ["location"] = "fsn1-dc14",
                ["description"] = "The box everything else sits on.",
                ["files"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["path"] = "sites/app-1.caddy",
                        ["directory"] = "/etc/caddy",
                        ["content"] = "app-1.example.test {\n  reverse_proxy app-1:8080\n}\n",
                    },
                },
                ["installations"] = new List<object>
                {
                    Installation("app-1", 443),
                    Installation("app-2", 444),
                },
            },
        },
        ["pages"] = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["slug"] = "backup-restore",
                ["title"] = "Restoring a backup",
                ["kind"] = "runbook",
                ["body"] = "Stop it, restore the volume, start it again.",
                ["attached_to"] = new Dictionary<string, object?> { ["kind"] = "machine", ["key"] = "ex44" },
            },
        },
    };

    private static Dictionary<string, object?> Installation(string key, int port) => new()
    {
        ["key"] = key,
        ["name"] = key,
        ["machine"] = "ex44",
        ["software"] = "logaffe",
        ["environment"] = "production",
        ["role"] = "application",
        ["path"] = $"/opt/compose/{key}",
        ["data"] = $"/srv/services/{key}",
        ["ports"] = new List<object>
        {
            new Dictionary<string, object?> { ["port"] = port, ["protocol"] = "tcp", ["scope"] = "public" },
        },
        ["secrets"] = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "DB_PASSWORD",
                ["path"] = $"/opt/compose/{key}/.env.runtime",
            },
        },
        ["files"] = key == "app-1"
            ? new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["path"] = "compose.yml",
                    ["content"] = "services:\n  app:\n    image: logaffe:1.4.0\n",
                },
            }
            : new List<object>
            {
                new Dictionary<string, object?> { ["path"] = ".env.example", ["content"] = "APP_DB_PASSWORD=\n" },
            },
        ["deployments"] = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["version"] = "1.4.0",
                ["at"] = "2026-09-05T12:00:00Z",
                ["ticket"] = "LOG-42",
                ["note"] = "Rolled forward.",
            },
        },
    };
}
