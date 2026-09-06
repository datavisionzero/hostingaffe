using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The two ends of a record (<c>docs/api.md</c>, Retiring and deleting):
/// retiring keeps everything and leaves the list, deleting takes what hangs
/// below and is undone for the grace period, and the history and the key
/// survive both.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class RecordDeletionTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_retired_machine_leaves_the_list_and_stays_reachable_by_its_key()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var retired = await admin.PatchAsJsonAsync("/api/machines/ex44", new { status = "retired" }, Ct);
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);

        Assert.DoesNotContain("ex44", await Keys(admin, "/api/machines"));
        Assert.Contains("ex44", await Keys(admin, "/api/machines?retired=true"));
        Assert.Equal(["ex44"], await Keys(admin, "/api/machines?status=retired"));

        // And it keeps everything, by its key, as always.
        var machine = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        Assert.Equal("retired", machine.GetProperty("status").GetString());
        Assert.Equal(["logaffe-prod"], await Keys(admin, "/api/installations?machine=ex44"));
    }

    [Fact]
    public async Task A_retired_installation_leaves_its_list_too()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var retired = await admin.PatchAsJsonAsync(
            "/api/installations/logaffe-prod", new { status = "retired" }, Ct);
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);

        Assert.Empty(await Keys(admin, "/api/installations"));
        Assert.Equal(["logaffe-prod"], await Keys(admin, "/api/installations?retired=true"));
        Assert.Equal(["logaffe-prod"], await Keys(admin, "/api/installations?status=retired"));
    }

    [Fact]
    public async Task A_deleted_machine_takes_everything_on_it_and_brings_it_all_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var deleted = await admin.DeleteAsync("/api/machines/ex44", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Everything under it is gone at once, and says so with restorable_until.
        foreach (var address in new[]
        {
            "/api/machines/ex44",
            "/api/installations/logaffe-prod",
            "/api/installations/logaffe-prod/files/compose.override.yml",
            "/api/installations/logaffe-prod/deployments/1",
        })
        {
            using var response = await admin.GetAsync(address, Ct);
            var problem = await Refusals.Problem(response, HttpStatusCode.NotFound, "deleted");
            Assert.True(problem.TryGetProperty("restorable_until", out _), address);
        }

        // The page stays, still naming the machine it hung on.
        var page = await admin.GetFromJsonAsync<JsonElement>("/api/pages/backup-restore", Ct);
        Assert.Equal("ex44", page.GetProperty("attached_to").GetProperty("key").GetString());

        using var restored = await admin.PostAsync("/api/machines/ex44/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);

        foreach (var address in new[]
        {
            "/api/machines/ex44",
            "/api/installations/logaffe-prod",
            "/api/installations/logaffe-prod/files/compose.override.yml",
            "/api/installations/logaffe-prod/deployments/1",
        })
        {
            using var response = await admin.GetAsync(address, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_restore_brings_back_what_that_deletion_took_and_nothing_else()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        // Deleted on its own, before the machine went.
        using var alone = await admin.DeleteAsync(
            "/api/installations/logaffe-prod/files/compose.override.yml", Ct);
        Assert.Equal(HttpStatusCode.NoContent, alone.StatusCode);

        using var deleted = await admin.DeleteAsync("/api/machines/ex44", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var restored = await admin.PostAsync("/api/machines/ex44/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);

        // The machine is back; the file somebody deleted by hand is not.
        using var machine = await admin.GetAsync("/api/machines/ex44", Ct);
        Assert.Equal(HttpStatusCode.OK, machine.StatusCode);

        using var file = await admin.GetAsync("/api/installations/logaffe-prod/files/compose.override.yml", Ct);
        await Refusals.Problem(file, HttpStatusCode.NotFound, "deleted");
    }

    [Fact]
    public async Task A_software_with_installations_is_not_deleted()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var refused = await admin.DeleteAsync("/api/software/logaffe", Ct);
        var problem = await Refusals.Problem(refused, HttpStatusCode.UnprocessableEntity, "transition");
        Assert.Equal(1, problem.GetProperty("installations").GetInt32());

        // The installation gone, the software goes.
        using var installation = await admin.DeleteAsync("/api/installations/logaffe-prod", Ct);
        Assert.Equal(HttpStatusCode.NoContent, installation.StatusCode);

        using var deleted = await admin.DeleteAsync("/api/software/logaffe", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Restoring_what_is_not_deleted_is_a_transition()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var machine = await admin.PostAsync("/api/machines/ex44/restore", null, Ct);
        await Refusals.Problem(machine, HttpStatusCode.UnprocessableEntity, "transition");

        using var deployment = await admin.PostAsync(
            "/api/installations/logaffe-prod/deployments/1/restore", null, Ct);
        await Refusals.Problem(deployment, HttpStatusCode.UnprocessableEntity, "transition");
    }

    [Fact]
    public async Task A_deleted_deployment_leaves_the_version_to_the_one_before_it()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var second = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/deployments", new { version = "1.1.0" }, Ct);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("1.1.0", await Version(admin));

        // Recorded with the wrong version: deleted, and recorded again.
        using var deleted = await admin.DeleteAsync("/api/installations/logaffe-prod/deployments/2", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal("1.0.0", await Version(admin));

        // And its number is not handed out again.
        using var third = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/deployments", new { version = "1.2.0" }, Ct);
        var recorded = await third.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(3, recorded.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task The_history_of_a_deleted_thing_still_says_that_it_existed()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var deleted = await admin.DeleteAsync("/api/machines/ex44", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The rows are there whatever the endpoints answer: the history is
        // never deleted, not even with the thing it describes (VISION 7).
        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        var entries = await reader.History
            .Where(entry => entry.Field == "deleted")
            .Select(entry => entry.Subject.ToString() + ":" + (entry.Note ?? string.Empty))
            .ToListAsync(Ct);

        Assert.Contains("Machine:", entries);
        Assert.Contains("Installation:with machine ex44", entries);
        Assert.Contains("File:with installation logaffe-prod", entries);
        Assert.Contains("Deployment:with installation logaffe-prod", entries);
    }

    [Fact]
    public async Task A_key_that_was_given_out_is_never_given_out_again()
    {
        var configuration = new Dictionary<string, string?>
        {
            // The shortest grace the instance accepts — under ten milliseconds
            // — so that the next write purges what this test deleted.
            ["HOSTINGAFFE_DELETION_GRACE_DAYS"] = "0.0000001",
        };

        await using var instance = await AnInstance.ConfiguredAsync(postgres, configuration);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var deleted = await admin.DeleteAsync("/api/machines/ex44", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Any write purges what is past its grace, and the grace is nothing.
        using var other = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "cx22", kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        Assert.False(await reader.Machines.AnyAsync(m => m.Key == "ex44", Ct));

        // The row is gone for good, and the key is still spent.
        using var again = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        var problem = await Refusals.Problem(again, HttpStatusCode.BadRequest, "validation");
        Assert.Contains("never given out twice", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        // And the history of the machine that is gone still says it existed.
        Assert.NotEmpty(await reader.History.Where(entry => entry.Field == "created").ToListAsync(Ct));
    }

    private static async Task<string?> Version(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/installations/logaffe-prod", Ct))
            .GetProperty("version").GetString();

    private static async Task<IReadOnlyList<string>> Keys(HttpClient client, string address) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(address, Ct))
            .EnumerateArray().Select(row => row.GetProperty("key").GetString()!)];

    /// <summary>A machine with everything the record can hang under it.</summary>
    private static async Task Ground(HttpClient client)
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
                version = "1.0.0",
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, installation.StatusCode);

        using var file = await client.PostAsJsonAsync(
            "/api/installations/logaffe-prod/files",
            new { path = "compose.override.yml", content = "services:" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, file.StatusCode);

        using var page = await client.PostAsJsonAsync(
            "/api/pages",
            new
            {
                slug = "backup-restore",
                title = "Backup and restore",
                kind = "runbook",
                attached_to = new { kind = "machine", key = "ex44" },
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, page.StatusCode);
    }
}
