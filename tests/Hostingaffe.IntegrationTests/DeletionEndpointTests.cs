using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Infrastructure.Persistence;

namespace Hostingaffe.IntegrationTests;

/// <summary>Deleting, restoring and the purge (ADR 0013, <c>docs/storage.md</c>).</summary>
[Collection(nameof(PostgresCollection))]
public sealed class DeletionEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_deleted_page_is_absent_everywhere_and_comes_back_under_the_slug_it_kept()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Page(admin, "architecture", "Architecture");
        await Page(admin, "operations", "Operations");

        using var deleted = await admin.DeleteAsync("/projects/PLAN/pages/architecture", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Absent from the read and from the list, and the slug stays spent.
        var problem = await ProjectEndpointTests.Problem(
            await admin.GetAsync("/projects/PLAN/pages/architecture", Ct), HttpStatusCode.NotFound, "deleted");
        Assert.True(problem.TryGetProperty("restorable_until", out _));

        var listed = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages", Ct);
        Assert.Equal(["operations"], listed.EnumerateArray().Select(p => p.GetProperty("slug").GetString()));

        await ProjectEndpointTests.Problem(
            await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Again" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        // Back, under the slug nobody could take meanwhile.
        using var restored = await admin.PostAsync("/projects/PLAN/pages/architecture/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal("architecture", (await restored.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slug").GetString());

        await ProjectEndpointTests.Problem(
            await admin.PostAsync("/projects/PLAN/pages/architecture/restore", null, Ct),
            HttpStatusCode.UnprocessableEntity, "transition");
        await ProjectEndpointTests.Problem(
            await admin.PostAsync("/projects/PLAN/pages/nowhere/restore", null, Ct),
            HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task The_purge_takes_rows_past_the_grace_period_on_the_next_write_to_their_project_and_no_other()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects", new { key = "OTHER", name = "other" }, Ct);
        await Page(admin, "old", "Old");
        await Page(admin, "recent", "Recent");
        using var elsewhere = await admin.PostAsJsonAsync(
            "/projects/OTHER/pages", new { slug = "elsewhere", title = "Elsewhere" }, Ct);

        await admin.DeleteAsync("/projects/PLAN/pages/old", Ct);
        await admin.DeleteAsync("/projects/PLAN/pages/recent", Ct);
        await admin.DeleteAsync("/projects/OTHER/pages/elsewhere", Ct);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            // PLAN/old and OTHER/elsewhere are eight days gone; PLAN/recent was deleted just now.
            await context.Database.ExecuteSqlRawAsync(
                "update page set deleted_at = now() - interval '8 days' where slug in ('old', 'elsewhere')", Ct);
            context.Idempotency.Add(IdempotencyRecord.Of(
                (await context.Users.SingleAsync(Ct)).Id, "old", new byte[32], 201, null, Migrated.Now.AddDays(-2)));
            await context.SaveChangesAsync(Ct);
        }

        // A write in PLAN: PLAN/old goes with its history; PLAN/recent stays; OTHER/elsewhere stays.
        await Page(admin, "a-write", "A write");

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            Assert.Equal(
                ["a-write", "elsewhere", "recent"],
                await context.Pages.Select(p => p.Slug).OrderBy(s => s).ToListAsync(Ct));
            Assert.Equal(0, await context.Idempotency.CountAsync(Ct));
            // The history died with its subject: one page id in it per surviving page, no orphans.
            Assert.Equal(
                await context.Pages.CountAsync(Ct),
                await context.History.Select(h => h.PageId).Distinct().CountAsync(Ct));
        }

        // A deleted project past its grace period goes with everything in it, on a write anywhere.
        await admin.DeleteAsync("/projects/OTHER", Ct);
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync(
                "update project set deleted_at = now() - interval '8 days' where key = 'OTHER'", Ct);
        }

        await Page(admin, "a-third-write", "A third write");
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            Assert.False(await context.Projects.AnyAsync(p => p.Key == "OTHER", Ct));
            Assert.False(await context.Pages.AnyAsync(p => p.Slug == "elsewhere", Ct));
        }

        // The key is free again.
        Assert.Equal(
            HttpStatusCode.Created,
            (await admin.PostAsJsonAsync("/projects", new { key = "OTHER", name = "other again" }, Ct)).StatusCode);
    }

    private static async Task<HttpClient> Project(AnInstance instance)
    {
        var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var project = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        return admin;
    }

    private static async Task Page(HttpClient admin, string slug, string title)
    {
        using var created = await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug, title }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }
}
