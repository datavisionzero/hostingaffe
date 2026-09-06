using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Domain.History;
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
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Page(admin, "architecture", "Architecture");
        await Page(admin, "operations", "Operations");

        using var deleted = await admin.DeleteAsync("/api/pages/architecture", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Absent from the read and from the list, and the slug stays spent.
        var problem = await Refusals.Problem(
            await admin.GetAsync("/api/pages/architecture", Ct), HttpStatusCode.NotFound, "deleted");
        Assert.True(problem.TryGetProperty("restorable_until", out _));

        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/pages", Ct);
        Assert.Equal(["operations"], listed.EnumerateArray().Select(p => p.GetProperty("slug").GetString()));

        await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Again" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        // Back, under the slug nobody could take meanwhile.
        using var restored = await admin.PostAsync("/api/pages/architecture/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal("architecture", (await restored.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slug").GetString());

        await Refusals.Problem(
            await admin.PostAsync("/api/pages/architecture/restore", null, Ct),
            HttpStatusCode.UnprocessableEntity, "transition");
        await Refusals.Problem(
            await admin.PostAsync("/api/pages/nowhere/restore", null, Ct),
            HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task The_purge_takes_rows_past_the_grace_period_on_the_next_write_and_leaves_the_rest()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Page(admin, "old", "Old");
        await Page(admin, "recent", "Recent");

        await admin.DeleteAsync("/api/pages/old", Ct);
        await admin.DeleteAsync("/api/pages/recent", Ct);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            // `old` is eight days gone; `recent` was deleted just now.
            await context.Database.ExecuteSqlRawAsync(
                "update page set deleted_at = now() - interval '8 days' where slug = 'old'", Ct);
            context.Idempotency.Add(IdempotencyRecord.Of(
                (await context.Users.SingleAsync(Ct)).Id, "old", new byte[32], 201, null, Migrated.Now.AddDays(-2)));
            await context.SaveChangesAsync(Ct);
        }

        // Any write pays for the purge: `old` goes, `recent` stays.
        await Page(admin, "a-write", "A write");

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            Assert.Equal(
                ["a-write", "recent"],
                await context.Pages.Select(p => p.Slug).OrderBy(s => s).ToListAsync(Ct));
            Assert.Equal(0, await context.Idempotency.CountAsync(Ct));

            // The history outlived its subject (VISION 7): the purged page is
            // gone and its rows still say that it existed and when it went.
            var subjects = await context.History
                .Where(h => h.Subject == HistorySubject.Page)
                .Select(h => h.SubjectId)
                .Distinct()
                .CountAsync(Ct);
            Assert.Equal(await context.Pages.CountAsync(Ct) + 1, subjects);
        }

        // The slug is free again.
        await Page(admin, "old", "Old again");
    }

    private static async Task Page(HttpClient admin, string slug, string title)
    {
        using var created = await admin.PostAsJsonAsync("/api/pages", new { slug, title }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }
}
