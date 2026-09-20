using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Domain;

namespace Hostingaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class InstallationPurgeTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_deleted_installation_can_be_purged_and_its_key_reused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(client);

        using var active = await client.PostAsync("/api/installations/app/purge?confirm=app", null, Ct);
        await Refusals.Problem(active, HttpStatusCode.UnprocessableEntity, "transition");

        using var deleted = await client.DeleteAsync("/api/installations/app", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var unconfirmed = await client.PostAsync("/api/installations/app/purge", null, Ct);
        await Refusals.Problem(unconfirmed, HttpStatusCode.BadRequest, "validation");

        using var purged = await client.PostAsync("/api/installations/app/purge?confirm=app", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, purged.StatusCode);

        await using (var db = Migrated.ContextFor(instance.ConnectionString))
        {
            Assert.False(await db.Installations.AnyAsync(i => i.Key == "app", Ct));
            Assert.False(await db.AssignedKeys.AnyAsync(k => k.Kind == Keyed.Installation && k.Key == "app", Ct));
            Assert.False(await db.Files.AnyAsync(f => f.InstallationId != null, Ct));
            Assert.False(await db.Deployments.AnyAsync(Ct));
        }

        using var recreated = await client.PostAsJsonAsync("/api/installations", new
        {
            key = "app", machine = "host-01", software = "sample",
            environment = "production", role = "application",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);

        var history = await client.GetFromJsonAsync<JsonElement>("/api/machines/host-01/history", Ct);
        Assert.Contains(history.EnumerateArray(), row =>
            row.GetProperty("field").GetString() == "installation_purged" &&
            row.GetProperty("new_value").GetString() == "app");
    }

    [Fact]
    public async Task A_purge_names_pages_and_links_that_must_be_cleared_first()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(client);

        using var page = await client.PostAsJsonAsync("/api/pages", new
        {
            slug = "app-guide", title = "App guide", kind = "runbook",
            attached_to = new { kind = "installation", key = "app" },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, page.StatusCode);

        using var linked = await client.PatchAsJsonAsync("/api/machines/host-01", new
        {
            description = "See [the app](installation:app#runbook).",
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);

        using var linkPage = await client.PostAsJsonAsync("/api/pages", new
        {
            slug = "links", title = "Links", kind = "runbook",
            body = "See [the app](installation:app).",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, linkPage.StatusCode);

        using var deleted = await client.DeleteAsync("/api/installations/app", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var refused = await client.PostAsync("/api/installations/app/purge?confirm=app", null, Ct);
        var problem = await Refusals.Problem(refused, HttpStatusCode.UnprocessableEntity, "transition");
        var references = problem.GetProperty("references").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("page app-guide (attached)", references);
        Assert.Contains("page links (link)", references);
        Assert.Contains("machine host-01 (link)", references);

        using var still = await client.GetAsync("/api/installations/app", Ct);
        await Refusals.Problem(still, HttpStatusCode.NotFound, "deleted");
    }

    [Fact]
    public async Task A_key_remains_releasable_after_the_automatic_sweep()
    {
        await using var instance = await AnInstance.ConfiguredAsync(postgres, new Dictionary<string, string?>
        {
            ["HOSTINGAFFE_DELETION_GRACE_DAYS"] = "0.0000001",
        });
        using var client = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(client);

        using var deleted = await client.DeleteAsync("/api/installations/app", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var sweep = await client.PostAsJsonAsync("/api/pages", new
        {
            slug = "another-page", title = "Another page", kind = "runbook",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, sweep.StatusCode);

        await using (var db = Migrated.ContextFor(instance.ConnectionString))
        {
            Assert.False(await db.Installations.AnyAsync(i => i.Key == "app", Ct));
            Assert.True(await db.AssignedKeys.AnyAsync(k => k.Kind == Keyed.Installation && k.Key == "app", Ct));
        }

        var attempts = await Task.WhenAll(
            client.PostAsync("/api/installations/app/purge?confirm=app", null, Ct),
            client.PostAsync("/api/installations/app/purge?confirm=app", null, Ct));
        try
        {
            Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound],
                attempts.Select(response => response.StatusCode).Order().ToArray());
        }
        finally
        {
            foreach (var response in attempts) response.Dispose();
        }

        using var recreated = await client.PostAsJsonAsync("/api/installations", new
        {
            key = "app", machine = "host-01", software = "sample",
            environment = "production", role = "application",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);

        await using var history = Migrated.ContextFor(instance.ConnectionString);
        Assert.Equal(1, await history.History.CountAsync(h => h.Field == "purged" && h.NewValue == "app", Ct));
    }

    [Fact]
    public async Task A_deleted_dependent_still_prevents_the_purge()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(client);

        using var dependent = await client.PostAsJsonAsync("/api/installations", new
        {
            key = "worker", machine = "host-01", software = "sample",
            environment = "production", role = "application",
            depends_on = new[] { "app" },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, dependent.StatusCode);

        using var firstDeleted = await client.DeleteAsync("/api/installations/worker", Ct);
        Assert.Equal(HttpStatusCode.NoContent, firstDeleted.StatusCode);
        using var secondDeleted = await client.DeleteAsync("/api/installations/app", Ct);
        Assert.Equal(HttpStatusCode.NoContent, secondDeleted.StatusCode);

        using var refused = await client.PostAsync("/api/installations/app/purge?confirm=app", null, Ct);
        var problem = await Refusals.Problem(refused, HttpStatusCode.UnprocessableEntity, "transition");
        Assert.Contains("installation worker (depends_on)",
            problem.GetProperty("references").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task A_machine_deletion_cannot_lose_one_of_its_installations_to_purge()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(client);

        using var deleted = await client.DeleteAsync("/api/machines/host-01", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var refused = await client.PostAsync("/api/installations/app/purge?confirm=app", null, Ct);
        var problem = await Refusals.Problem(refused, HttpStatusCode.UnprocessableEntity, "transition");
        Assert.Contains("machine host-01 (deleted)",
            problem.GetProperty("references").EnumerateArray().Select(value => value.GetString()));

        using var restored = await client.PostAsync("/api/machines/host-01/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var installation = await client.GetAsync("/api/installations/app", Ct);
        Assert.Equal(HttpStatusCode.OK, installation.StatusCode);
    }

    private static async Task Ground(HttpClient client)
    {
        using var machine = await client.PostAsJsonAsync("/api/machines", new { key = "host-01", kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.Created, machine.StatusCode);
        using var software = await client.PostAsJsonAsync("/api/software", new { key = "sample" }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);
        using var installation = await client.PostAsJsonAsync("/api/installations", new
        {
            key = "app", machine = "host-01", software = "sample",
            environment = "production", role = "application", version = "1.0.0",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, installation.StatusCode);
        using var file = await client.PostAsJsonAsync("/api/installations/app/files", new
        {
            path = "compose.yml", content = "services: {}",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, file.StatusCode);
    }
}
