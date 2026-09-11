using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Installations over HTTP (<c>docs/api.md</c>, Installations): the second and
/// last relationship the model builds, and the six closed sets that say what an
/// installation is.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InstallationEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_installation_is_a_software_on_a_machine_and_reads_back_whole()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var created = await admin.PostAsJsonAsync(
            "/api/installations",
            new
            {
                key = "logaffe-prod",
                name = "logaffe",
                machine = "ex44",
                software = "logaffe",
                environment = "production",
                role = "application",
                urls = new[] { "https://logs.example.test" },
                ports = new object[]
                {
                    new { port = 443, protocol = "tcp", scope = "public" },
                    new { port = 5432, protocol = "tcp", scope = "private" },
                },
                path = "/opt/compose/logaffe",
                data = "/srv/services/logaffe",
                secrets = new object[]
                {
                    new { name = "LOGAFFE_DB_PASSWORD", path = "/opt/compose/logaffe/.env.runtime" },
                },
                backup = "active",
                monitoring = "external",
                logging = "central",
                description = "The runbook.",
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/installations/logaffe-prod", created.Headers.Location?.ToString());

        var installation = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod", Ct);
        Assert.Equal("ex44", installation.GetProperty("machine").GetString());
        Assert.Equal("logaffe", installation.GetProperty("software").GetString());
        Assert.Equal("production", installation.GetProperty("environment").GetString());
        Assert.Equal("application", installation.GetProperty("role").GetString());
        Assert.Equal("/opt/compose/logaffe", installation.GetProperty("path").GetString());
        Assert.Equal("/srv/services/logaffe", installation.GetProperty("data").GetString());
        var secrets = installation.GetProperty("secrets");
        Assert.Equal(1, secrets.GetArrayLength());
        Assert.Equal("LOGAFFE_DB_PASSWORD", secrets[0].GetProperty("name").GetString());
        Assert.Equal("/opt/compose/logaffe/.env.runtime", secrets[0].GetProperty("path").GetString());

        var ports = installation.GetProperty("ports");
        Assert.Equal(2, ports.GetArrayLength());
        Assert.Equal(443, ports[0].GetProperty("port").GetInt32());
        Assert.Equal("tcp", ports[0].GetProperty("protocol").GetString());
        Assert.Equal("public", ports[0].GetProperty("scope").GetString());

        // Its version is derived and nothing recorded it yet, so it is empty —
        // not absent. What fills it is a deployment.
        Assert.Equal(JsonValueKind.Null, installation.GetProperty("version").ValueKind);
    }

    [Fact]
    public async Task The_defaults_are_the_ones_that_are_true()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        var installation = await Installation(admin, "caddy-proxy", role: "platform");

        Assert.Equal("active", installation.GetProperty("status").GetString());
        Assert.Equal("none", installation.GetProperty("backup").GetString());
        Assert.Equal("none", installation.GetProperty("monitoring").GetString());
        Assert.Equal("local", installation.GetProperty("logging").GetString());
        Assert.Empty(installation.GetProperty("urls").EnumerateArray());
        Assert.Empty(installation.GetProperty("ports").EnumerateArray());
    }

    [Fact]
    public async Task A_machine_or_a_software_that_does_not_exist_is_refused_on_the_field_it_arrived_in()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var nowhere = await admin.PostAsJsonAsync(
            "/api/installations",
            new { key = "x", machine = "nowhere", software = "logaffe", environment = "production", role = "application" },
            Ct);
        var problem = await Refusals.Problem(nowhere, HttpStatusCode.BadRequest, "validation");
        Assert.Contains("machine", problem.GetProperty("errors").EnumerateObject().Select(field => field.Name));

        using var unknown = await admin.PostAsJsonAsync(
            "/api/installations",
            new { key = "x", machine = "ex44", software = "nothing", environment = "production", role = "application" },
            Ct);
        await Refusals.Problem(unknown, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task The_five_that_are_required_are_required()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var nothing = await admin.PostAsJsonAsync("/api/installations", new { key = "x" }, Ct);
        await Refusals.Problem(nothing, HttpStatusCode.BadRequest, "validation");

        using var noRole = await admin.PostAsJsonAsync(
            "/api/installations",
            new { key = "x", machine = "ex44", software = "logaffe", environment = "production" },
            Ct);
        var problem = await Refusals.Problem(noRole, HttpStatusCode.BadRequest, "validation");
        Assert.Contains("role", problem.GetProperty("errors").EnumerateObject().Select(field => field.Name));
    }

    [Fact]
    public async Task A_word_outside_a_closed_set_never_reaches_a_row()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        foreach (var body in new object[]
        {
            new { key = "x", machine = "ex44", software = "logaffe", environment = "prod", role = "application" },
            new { key = "x", machine = "ex44", software = "logaffe", environment = "production", role = "infrastructure" },
        })
        {
            using var refused = await admin.PostAsJsonAsync("/api/installations", body, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        await Installation(admin, "logaffe-prod");

        foreach (var body in new object[]
        {
            new { status = "gone" },
            new { backup = "maybe" },
            new { monitoring = "internal" },
            new { logging = "remote" },
        })
        {
            using var refused = await admin.PatchAsJsonAsync("/api/installations/logaffe-prod", body, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
    }

    [Fact]
    public async Task A_port_is_an_object_and_the_same_one_twice_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Installation(admin, "logaffe-prod");

        using var twice = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new
            {
                ports = new object[]
                {
                    new { port = 443, protocol = "tcp", scope = "public" },
                    new { port = 443, protocol = "tcp", scope = "private" },
                },
            },
            Ct);
        await Refusals.Problem(twice, HttpStatusCode.BadRequest, "validation");

        using var outOfRange = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new { ports = new object[] { new { port = 0, protocol = "tcp", scope = "public" } } },
            Ct);
        await Refusals.Problem(outOfRange, HttpStatusCode.BadRequest, "validation");

        using var notATransport = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new { ports = new object[] { new { port = 443, protocol = "sctp", scope = "public" } } },
            Ct);
        Assert.Equal(HttpStatusCode.BadRequest, notATransport.StatusCode);
    }

    [Fact]
    public async Task A_secret_is_named_and_never_given()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Installation(admin, "logaffe-prod");

        using var given = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new { secrets = new object[] { new { name = "LOGAFFE_DB_PASSWORD=hunter2" } } },
            Ct);
        var problem = await Refusals.Problem(given, HttpStatusCode.BadRequest, "validation");
        Assert.Contains("secrets", problem.GetProperty("errors").EnumerateObject().Select(field => field.Name));

        // The name is one half; the other is a file on the machine, and a
        // relative one is no more an answer here than it is for a path.
        using var nowhere = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new { secrets = new object[] { new { name = "LOGAFFE_DB_PASSWORD", path = "compose/.env" } } },
            Ct);
        await Refusals.Problem(nowhere, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task A_secret_says_which_file_it_lies_in_and_the_history_reads_it_whole()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Installation(admin, "logaffe-prod");

        using var written = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new
            {
                secrets = new object[]
                {
                    new { name = "LOGAFFE_DB_PASSWORD", path = "/opt/compose/logaffe/.env.runtime" },
                    new { name = "SMTP_PASSWORD" },
                },
            },
            Ct);
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);

        var installation = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod", Ct);
        var secrets = installation.GetProperty("secrets");
        Assert.Equal("/opt/compose/logaffe/.env.runtime", secrets[0].GetProperty("path").GetString());

        // A secret whose place nobody has decided is a secret the installation
        // still needs, and the field says so by being empty rather than absent.
        Assert.Equal("SMTP_PASSWORD", secrets[1].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, secrets[1].GetProperty("path").ValueKind);

        // A history row is text a person reads, and this is how a person writes
        // a secret down.
        var history = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod/history", Ct);
        var written_ = history.EnumerateArray().Last();
        Assert.Equal("secrets", written_.GetProperty("field").GetString());
        Assert.Equal(
            "LOGAFFE_DB_PASSWORD@/opt/compose/logaffe/.env.runtime, SMTP_PASSWORD",
            written_.GetProperty("new_value").GetString());

        // An installation needs a secret once. Two entries naming the same one
        // are a contradiction, not two secrets.
        using var twice = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new
            {
                secrets = new object[]
                {
                    new { name = "LOGAFFE_DB_PASSWORD", path = "/opt/compose/logaffe/.env.runtime" },
                    new { name = "LOGAFFE_DB_PASSWORD", path = "/srv/services/logaffe/.env" },
                },
            },
            Ct);
        await Refusals.Problem(twice, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task An_installation_has_two_directories_and_the_second_is_where_its_data_lies()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Installation(admin, "logaffe-prod");

        using var written = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new { path = "/opt/compose/logaffe", data = "/srv/services/logaffe/" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);

        var installation = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod", Ct);
        Assert.Equal("/srv/services/logaffe", installation.GetProperty("data").GetString());

        // Relative is refused on the field it arrived in: the two directories
        // hold the same shape and each answers for itself.
        using var relative = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod", new { data = "srv/services/logaffe" }, Ct);
        var problem = await Refusals.Problem(relative, HttpStatusCode.BadRequest, "validation");
        Assert.Contains("data", problem.GetProperty("errors").EnumerateObject().Select(field => field.Name));

        // The empty string clears it; the history says both moves.
        using var cleared = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod", new { data = "" }, Ct);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod/history", Ct);
        var data = history.EnumerateArray()
            .Where(entry => entry.GetProperty("field").GetString() == "data")
            .ToArray();
        Assert.Equal(2, data.Length);
        Assert.Equal("/srv/services/logaffe", data[0].GetProperty("new_value").GetString());
        Assert.Equal(JsonValueKind.Null, data[1].GetProperty("new_value").ValueKind);
    }

    [Fact]
    public async Task Every_production_installation_without_a_backup_is_one_call()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        await Installation(admin, "logaffe-prod");
        await Installation(admin, "logaffe-staging", environment: "staging");
        await Installation(admin, "caddy-proxy", role: "platform", rest: new { backup = "active" });

        var unprotected = await admin.GetFromJsonAsync<JsonElement>(
            "/api/installations?environment=production&backup=none", Ct);

        Assert.Equal(["logaffe-prod"], unprotected.EnumerateArray().Select(i => i.GetProperty("key").GetString()!));

        var onTheMachine = await admin.GetFromJsonAsync<JsonElement>("/api/installations?machine=ex44", Ct);
        Assert.Equal(3, onTheMachine.GetArrayLength());

        // Slim is slim.
        Assert.False(onTheMachine[0].TryGetProperty("description", out _));
        Assert.False(onTheMachine[0].TryGetProperty("ports", out _));

        using var nonsense = await admin.GetAsync("/api/installations?environment=prod", Ct);
        await Refusals.Problem(nonsense, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task The_history_carries_every_change_and_a_list_reads_as_its_entries()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Installation(admin, "logaffe-prod");

        using var changed = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod",
            new
            {
                backup = "active",
                ports = new object[] { new { port = 443, protocol = "tcp", scope = "public" } },
                description = "The runbook.",
            },
            Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod/history", Ct);
        var fields = history.EnumerateArray().Select(entry => entry.GetProperty("field").GetString()!);
        Assert.Equal(["created", "backup", "ports", "description"], fields);

        var ports = history.EnumerateArray().Single(entry => entry.GetProperty("field").GetString() == "ports");
        Assert.Equal("443/tcp:public", ports.GetProperty("new_value").GetString());
    }

    [Fact]
    public async Task The_key_is_immutable_and_the_two_fields_the_model_does_not_have_say_why()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Installation(admin, "logaffe-prod");

        using var renamed = await admin.PatchAsJsonAsync("/api/installations/logaffe-prod", new { key = "other" }, Ct);
        await Refusals.Problem(renamed, HttpStatusCode.BadRequest, "unknown-field");

        using var versioned = await admin.PatchAsJsonAsync("/api/installations/logaffe-prod", new { version = "1.4.0" }, Ct);
        var version = await Refusals.Problem(versioned, HttpStatusCode.BadRequest, "unknown-field");
        Assert.Contains("derived", version.GetProperty("detail").GetString(), StringComparison.Ordinal);

        using var depends = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod", new { depends_on = "postgres-prod" }, Ct);
        await Refusals.Problem(depends, HttpStatusCode.BadRequest, "unknown-field");
    }

    /// <summary>The machine and the software every installation here stands on.</summary>
    private static async Task Ground(HttpClient client)
    {
        using var machine = await client.PostAsJsonAsync("/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, machine.StatusCode);

        using var software = await client.PostAsJsonAsync("/api/software", new { key = "logaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);
    }

    private static async Task<JsonElement> Installation(
        HttpClient client,
        string key,
        string environment = "production",
        string role = "application",
        object? rest = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["key"] = key,
            ["machine"] = "ex44",
            ["software"] = "logaffe",
            ["environment"] = environment,
            ["role"] = role,
        };

        foreach (var property in rest?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(rest);
        }

        using var created = await client.PostAsJsonAsync("/api/installations", body, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
