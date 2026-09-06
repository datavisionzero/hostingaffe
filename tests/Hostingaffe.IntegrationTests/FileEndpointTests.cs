using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Files over HTTP (<c>docs/api.md</c>, Files): under a machine and under an
/// installation, with every earlier content still readable.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FileEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_file_belongs_to_a_machine_or_to_an_installation()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        var unit = await Put(admin, "/api/machines/ex44/files", "systemd/logaffe.service", "[Unit]");
        Assert.Equal("machine", unit.GetProperty("owner").GetProperty("kind").GetString());
        Assert.Equal("ex44", unit.GetProperty("owner").GetProperty("key").GetString());
        Assert.Equal(1, unit.GetProperty("revision").GetInt32());

        var compose = await Put(admin, "/api/installations/logaffe-prod/files", "compose.override.yml", "services:");
        Assert.Equal("installation", compose.GetProperty("owner").GetProperty("kind").GetString());

        // The same path under two owners is two files, and neither is the other's.
        await Put(admin, "/api/machines/ex44/files", "compose.override.yml", "on the machine");

        var onTheMachine = await admin.GetFromJsonAsync<JsonElement>(
            "/api/machines/ex44/files/compose.override.yml", Ct);
        Assert.Equal("on the machine", onTheMachine.GetProperty("content").GetString());

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod/files", Ct);
        Assert.Equal(["compose.override.yml"], list.EnumerateArray().Select(f => f.GetProperty("path").GetString()!));
        Assert.False(list[0].TryGetProperty("content", out _));
    }

    [Fact]
    public async Task Every_write_is_a_revision_and_every_earlier_one_stays_readable()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Put(admin, "/api/installations/logaffe-prod/files", "compose.override.yml", "one");

        await Write(admin, "two");
        await Write(admin, "three");

        var now = await admin.GetFromJsonAsync<JsonElement>(Address, Ct);
        Assert.Equal(3, now.GetProperty("revision").GetInt32());
        Assert.Equal("three", now.GetProperty("content").GetString());

        var first = await admin.GetFromJsonAsync<JsonElement>($"{Address}?revision=1", Ct);
        Assert.Equal("one", first.GetProperty("content").GetString());
        Assert.Equal(1, first.GetProperty("revision").GetInt32());

        var revisions = await admin.GetFromJsonAsync<JsonElement>(
            "/api/installations/logaffe-prod/file-revisions/compose.override.yml", Ct);
        Assert.Equal([3, 2, 1], revisions.EnumerateArray().Select(r => r.GetProperty("revision").GetInt32()));
        Assert.False(revisions[0].TryGetProperty("content", out _));

        using var never = await admin.GetAsync($"{Address}?revision=9", Ct);
        Assert.Equal(HttpStatusCode.NotFound, never.StatusCode);
    }

    [Fact]
    public async Task A_write_that_changes_nothing_makes_no_revision()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Put(admin, "/api/installations/logaffe-prod/files", "compose.override.yml", "one");

        var again = await Write(admin, "one");
        Assert.Equal(1, again.GetProperty("revision").GetInt32());

        var bit = await Write(admin, null, executable: true);
        Assert.Equal(2, bit.GetProperty("revision").GetInt32());
        Assert.Equal("one", bit.GetProperty("content").GetString());
        Assert.True(bit.GetProperty("executable").GetBoolean());
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.production")]
    [InlineData("secrets/token")]
    [InlineData("stacks/secrets/token")]
    [InlineData("../elsewhere")]
    [InlineData("/etc/passwd")]
    public async Task Every_refused_path_is_refused_at_the_api(string path)
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var refused = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/files", new { path, content = "x" }, Ct);
        await Refusals.Problem(refused, HttpStatusCode.BadRequest, "validation");
    }

    [Theory]
    [InlineData(".envrc")]
    [InlineData(".env.example")]
    public async Task What_carries_no_value_is_welcome(string path)
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        await Put(admin, "/api/installations/logaffe-prod/files", path, "use flake");
    }

    [Fact]
    public async Task A_content_over_a_megabyte_and_a_content_that_is_not_text_are_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var big = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/files",
            new { path = "big.yml", content = new string('x', (1024 * 1024) + 1) },
            Ct);
        await Refusals.Problem(big, HttpStatusCode.BadRequest, "validation");

        // A lone half of a surrogate pair is a string that is no sequence of
        // characters, and so no UTF-8 either. It has to be sent as raw bytes: a
        // JSON writer replaces it with a replacement character on the way out,
        // which is exactly what the instance must not do on the way in.
        using var raw = new StringContent(
            """{"path": "broken.yml", "content": "one\ud800two"}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using var notText = await admin.PostAsync("/api/installations/logaffe-prod/files", raw, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, notText.StatusCode);

        using var withNull = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/files",
            new { path = "null.yml", content = "one\0two" },
            Ct);
        await Refusals.Problem(withNull, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task A_path_is_unique_per_owner()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Put(admin, "/api/installations/logaffe-prod/files", "compose.override.yml", "one");

        using var again = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/files",
            new { path = "compose.override.yml", content = "two" },
            Ct);
        await Refusals.Problem(again, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task A_file_does_not_move_and_a_revision_is_not_given()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Put(admin, "/api/installations/logaffe-prod/files", "compose.override.yml", "one");

        using var moved = await admin.PutAsJsonAsync(Address, new { path = "other.yml" }, Ct);
        await Refusals.Problem(moved, HttpStatusCode.BadRequest, "unknown-field");

        using var numbered = await admin.PutAsJsonAsync(Address, new { revision = 7 }, Ct);
        await Refusals.Problem(numbered, HttpStatusCode.BadRequest, "unknown-field");
    }

    [Fact]
    public async Task The_history_says_which_revision_it_became()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        await Put(admin, "/api/installations/logaffe-prod/files", "compose.override.yml", "one");
        await Write(admin, "two");

        var history = await admin.GetFromJsonAsync<JsonElement>(
            "/api/installations/logaffe-prod/file-history/compose.override.yml", Ct);

        Assert.Equal(["created", "revision"], history.EnumerateArray().Select(e => e.GetProperty("field").GetString()!));
        Assert.Equal("compose.override.yml", history[0].GetProperty("new_value").GetString());
        Assert.Equal("1", history[1].GetProperty("old_value").GetString());
        Assert.Equal("2", history[1].GetProperty("new_value").GetString());
    }

    [Fact]
    public async Task A_write_over_a_version_somebody_moved_is_stale()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        var created = await Put(admin, "/api/installations/logaffe-prod/files", "compose.override.yml", "one");
        var read = created.GetProperty("updated_at").GetString();

        await Write(admin, "two");

        using var request = new HttpRequestMessage(HttpMethod.Put, Address)
        {
            Content = JsonContent.Create(new { content = "three" }),
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{read}\""));

        using var refused = await admin.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
    }

    [Fact]
    public async Task An_owner_that_names_nothing_is_not_found()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var nowhere = await admin.GetAsync("/api/machines/nowhere/files", Ct);
        Assert.Equal(HttpStatusCode.NotFound, nowhere.StatusCode);

        using var missing = await admin.GetAsync("/api/installations/logaffe-prod/files/nothing.yml", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private const string Address = "/api/installations/logaffe-prod/files/compose.override.yml";

    private static async Task Ground(HttpClient client)
    {
        using var machine = await client.PostAsJsonAsync("/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, machine.StatusCode);

        using var software = await client.PostAsJsonAsync("/api/software", new { key = "logaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);

        using var installation = await client.PostAsJsonAsync(
            "/api/installations",
            new { key = "logaffe-prod", machine = "ex44", software = "logaffe", environment = "production", role = "application" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, installation.StatusCode);
    }

    private static async Task<JsonElement> Put(HttpClient client, string under, string path, string content)
    {
        using var created = await client.PostAsJsonAsync(under, new { path, content }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> Write(HttpClient client, string? content, bool? executable = null)
    {
        using var written = await client.PutAsJsonAsync(Address, new { content, executable }, Ct);
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        return await written.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
