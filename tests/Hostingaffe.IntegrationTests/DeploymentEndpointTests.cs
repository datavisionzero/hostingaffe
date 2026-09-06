using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Deployments over HTTP (<c>docs/api.md</c>, Deployments), and the thing this
/// epic would fail on: everything derived is ordered by <c>at</c>, never by the
/// order of recording.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DeploymentEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_deployment_is_created_with_the_installation()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin, version: "1.0.0");

        var installation = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod", Ct);
        Assert.Equal("1.0.0", installation.GetProperty("version").GetString());

        var deployments = await admin.GetFromJsonAsync<JsonElement>(Address, Ct);
        var first = Assert.Single(deployments.EnumerateArray());
        Assert.Equal(1, first.GetProperty("number").GetInt32());
        Assert.Equal("1.0.0", first.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("previous").ValueKind);
    }

    [Fact]
    public async Task An_installation_without_a_version_has_none_and_no_deployment()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        var installation = await admin.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod", Ct);
        Assert.Equal(JsonValueKind.Null, installation.GetProperty("version").ValueKind);

        var deployments = await admin.GetFromJsonAsync<JsonElement>(Address, Ct);
        Assert.Empty(deployments.EnumerateArray());
    }

    [Fact]
    public async Task The_installation_reads_its_version_from_the_latest_deployment_by_at()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        // Without a version at creation, every `at` below is one this test set,
        // so nothing here depends on what the clock said.
        await Ground(admin);

        await Record(admin, "1.0.0", at: "2026-01-01T10:00:00Z");
        await Record(admin, "1.1.0", at: "2026-06-01T10:00:00Z");
        Assert.Equal("1.1.0", await Version(admin));

        // Backfilled, older than both: the present does not move.
        await Record(admin, "0.9.0", at: "2025-01-01T10:00:00Z");
        Assert.Equal("1.1.0", await Version(admin));

        // Backfilled, newer: it does.
        await Record(admin, "1.2.0", at: "2026-08-01T10:00:00Z");
        Assert.Equal("1.2.0", await Version(admin));

        // And the list is the same order, newest first.
        var deployments = await admin.GetFromJsonAsync<JsonElement>(Address, Ct);
        Assert.Equal(
            ["1.2.0", "1.1.0", "1.0.0", "0.9.0"],
            deployments.EnumerateArray().Select(one => one.GetProperty("version").GetString()!));

        // Previous walks the same order, and the numbers say it is not the
        // order they were recorded in.
        Assert.Equal([4, 2, 1, 3], deployments.EnumerateArray().Select(one => one.GetProperty("number").GetInt32()));
        Assert.Equal("1.1.0", deployments[0].GetProperty("previous").GetString());
        Assert.Equal(JsonValueKind.Null, deployments[3].GetProperty("previous").ValueKind);
    }

    [Fact]
    public async Task Previous_and_files_use_the_same_order()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin, version: "1.0.0");

        // A file, written twice, with the second write after the deployment we
        // are about to backfill.
        using var created = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/files",
            new { path = "compose.override.yml", content = "one" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var newest = await Record(admin, "1.1.0");
        var backfilled = await Record(admin, "0.9.0", at: "2026-01-01T10:00:00Z");

        var latest = await admin.GetFromJsonAsync<JsonElement>(
            $"{Address}/{newest.GetProperty("number").GetInt32()}", Ct);
        Assert.Equal("1.0.0", latest.GetProperty("previous").GetString());
        Assert.Equal(
            [("compose.override.yml", 1)],
            latest.GetProperty("files").EnumerateArray()
                .Select(file => (file.GetProperty("path").GetString()!, file.GetProperty("revision").GetInt32())));

        // Backfilled to before the first file was put: no previous, no files.
        var earliest = await admin.GetFromJsonAsync<JsonElement>(
            $"{Address}/{backfilled.GetProperty("number").GetInt32()}", Ct);
        Assert.Equal(JsonValueKind.Null, earliest.GetProperty("previous").ValueKind);
        Assert.Empty(earliest.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task A_deployment_is_numbered_by_the_instance()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin, version: "1.0.0");

        Assert.Equal(2, (await Record(admin, "1.1.0")).GetProperty("number").GetInt32());
        Assert.Equal(3, (await Record(admin, "1.2.0")).GetProperty("number").GetInt32());

        using var given = await admin.PostAsJsonAsync(Address, new { version = "1.3.0", number = 9 }, Ct);
        await Refusals.Problem(given, HttpStatusCode.BadRequest, "unknown-field");
    }

    [Fact]
    public async Task Only_the_four_fields_are_corrected()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin, version: "1.0.0");

        using var corrected = await admin.PatchAsJsonAsync(
            $"{Address}/1",
            new { @ref = "sha256:abcdef", ticket = "LOG-42", note = "Rolled forward." },
            Ct);
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        var deployment = await admin.GetFromJsonAsync<JsonElement>($"{Address}/1", Ct);
        Assert.Equal("sha256:abcdef", deployment.GetProperty("ref").GetString());
        Assert.Equal("LOG-42", deployment.GetProperty("ticket").GetString());

        using var versioned = await admin.PatchAsJsonAsync($"{Address}/1", new { version = "9.9.9" }, Ct);
        var problem = await Refusals.Problem(versioned, HttpStatusCode.BadRequest, "unknown-field");
        Assert.Contains("deleted and recorded again", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        using var moved = await admin.PatchAsJsonAsync($"{Address}/1", new { installation = "other" }, Ct);
        await Refusals.Problem(moved, HttpStatusCode.BadRequest, "unknown-field");

        using var status = await admin.PatchAsJsonAsync($"{Address}/1", new { status = "failed" }, Ct);
        var refused = await Refusals.Problem(status, HttpStatusCode.BadRequest, "unknown-field");
        Assert.Contains("recorded when it is done", refused.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_at_moves_the_version_and_the_history_says_so()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin, version: "1.0.0");
        await Record(admin, "1.1.0");

        Assert.Equal("1.1.0", await Version(admin));

        // The second one really ran a year ago, so the first is current again.
        using var corrected = await admin.PatchAsJsonAsync(
            $"{Address}/2", new { at = "2025-01-01T10:00:00Z" }, Ct);
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        Assert.Equal("1.0.0", await Version(admin));

        var history = await admin.GetFromJsonAsync<JsonElement>($"{Address}/2/history", Ct);
        Assert.Equal(["created", "at"], history.EnumerateArray().Select(e => e.GetProperty("field").GetString()!));
    }

    [Fact]
    public async Task A_deployment_that_was_never_recorded_is_not_found()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin, version: "1.0.0");

        using var missing = await admin.GetAsync($"{Address}/9", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var nowhere = await admin.GetAsync("/api/installations/nothing/deployments", Ct);
        Assert.Equal(HttpStatusCode.NotFound, nowhere.StatusCode);
    }

    [Fact]
    public async Task A_deployment_says_which_version_ran()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin, version: "1.0.0");

        using var nothing = await admin.PostAsJsonAsync(Address, new { note = "something happened" }, Ct);
        await Refusals.Problem(nothing, HttpStatusCode.BadRequest, "validation");
    }

    private const string Address = "/api/installations/logaffe-prod/deployments";

    private static async Task<string?> Version(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod", Ct))
            .GetProperty("version").GetString();

    private static async Task<JsonElement> Record(HttpClient client, string version, string? at = null)
    {
        using var recorded = await client.PostAsJsonAsync(Address, new { version, at }, Ct);
        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
        return await recorded.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task Ground(HttpClient client, string? version = null)
    {
        using var machine = await client.PostAsJsonAsync("/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, machine.StatusCode);

        using var software = await client.PostAsJsonAsync("/api/software", new { key = "logaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);

        using var installation = await client.PostAsJsonAsync(
            "/api/installations",
            new
            {
                key = "logaffe-prod",
                machine = "ex44",
                software = "logaffe",
                environment = "production",
                role = "application",
                version,
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, installation.StatusCode);
    }
}
