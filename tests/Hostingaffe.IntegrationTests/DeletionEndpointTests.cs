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
    public async Task A_deleted_issue_is_absent_everywhere_and_comes_back_without_its_claim()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var agent = await Agent(instance, admin, "one");
        await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = "Backend" }, Ct);
        await Issues(admin,
            new { @ref = "gone", title = "Gone", epic = "PLAN-E1" },
            new { title = "Blocked by gone", blocked_by = new[] { "gone" } },
            new { title = "Stays", epic = "PLAN-E1" });
        await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);

        // Before: PLAN-2 waits on PLAN-1, the epic counts two, next offers only PLAN-3.
        Assert.Equal(["PLAN-3"], await NextKeys(agent));
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>("/epics/PLAN-E1", Ct)).GetProperty("progress").GetProperty("total").GetInt32());

        using var deleted = await agent.DeleteAsync("/issues/PLAN-1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Absent: read, list, count, next, epic progress, and as a blocker.
        var problem = await ProjectEndpointTests.Problem(await admin.GetAsync("/issues/PLAN-1", Ct), HttpStatusCode.NotFound, "deleted");
        Assert.True(problem.TryGetProperty("restorable_until", out _));
        var listed = await admin.GetFromJsonAsync<JsonElement>("/issues?project=PLAN", Ct);
        Assert.Equal(2, listed.GetProperty("total").GetInt32());
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>("/epics/PLAN-E1", Ct)).GetProperty("progress").GetProperty("total").GetInt32());
        Assert.Equal(["PLAN-2", "PLAN-3"], await NextKeys(agent));
        var blocked = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-2", Ct);
        Assert.Empty(blocked.GetProperty("blocked_by").EnumerateArray());
        Assert.Equal(0, blocked.GetProperty("open_blockers").GetInt32());

        // The one read that sees it, with who deleted it.
        var inGrace = await admin.GetFromJsonAsync<JsonElement>("/issues?project=PLAN&deleted=true", Ct);
        var item = Assert.Single(inGrace.GetProperty("items").EnumerateArray());
        Assert.Equal("PLAN-1", item.GetProperty("key").GetString());
        Assert.Equal("one", item.GetProperty("deleted_by").GetProperty("name").GetString());
        Assert.Equal("todo", item.GetProperty("status").GetString());

        // Every act on it says deleted.
        await ProjectEndpointTests.Problem(await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct), HttpStatusCode.NotFound, "deleted");
        await ProjectEndpointTests.Problem(await agent.DeleteAsync("/issues/PLAN-1", Ct), HttpStatusCode.NotFound, "deleted");

        // Back, in todo, without the claim; the edge is back too.
        using var restored = await admin.PostAsync("/issues/PLAN-1/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var back = await restored.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("todo", back.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, back.GetProperty("claim").ValueKind);
        Assert.Equal(["PLAN-2"], back.GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("key").GetString()));
        Assert.Equal(["PLAN-1", "PLAN-3"], await NextKeys(agent));
        await ProjectEndpointTests.Problem(await admin.PostAsync("/issues/PLAN-1/restore", null, Ct), HttpStatusCode.UnprocessableEntity, "transition");
        await ProjectEndpointTests.Problem(await admin.PostAsync("/issues/PLAN-9/restore", null, Ct), HttpStatusCode.NotFound, "not-found");

        var history = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1/history", Ct);
        Assert.Equal(
            ["created", "claim", "status", "claim", "status", "deleted", "deleted"],
            history.EnumerateArray().Select(h => h.GetProperty("field").GetString()));
    }

    [Fact]
    public async Task The_purge_takes_rows_past_the_grace_period_on_the_next_write_to_their_project_and_no_other()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects", new { key = "OTHER", name = "other" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "old-label" }, Ct);
        await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = "Old epic" }, Ct);
        await Issues(admin, new { title = "Old", epic = "PLAN-E1" }, new { title = "Recent" });
        using var otherIssue = await admin.PostAsJsonAsync("/issues", new { project = "OTHER", issues = new[] { new { title = "Elsewhere" } } }, Ct);
        await admin.PostAsJsonAsync("/issues/PLAN-1/comments", new { body = "Dies with the row." }, Ct);

        await admin.DeleteAsync("/issues/PLAN-1", Ct);
        await admin.DeleteAsync("/issues/PLAN-2", Ct);
        await admin.DeleteAsync("/issues/OTHER-1", Ct);
        await admin.DeleteAsync("/projects/PLAN/labels/old-label", Ct);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            // PLAN-1, OTHER-1 and the label are eight days gone; PLAN-2 was deleted just now.
            await context.Database.ExecuteSqlRawAsync("update issue set deleted_at = now() - interval '8 days' where number = 1", Ct);
            await context.Database.ExecuteSqlRawAsync("update label set deleted_at = now() - interval '8 days' where name = 'old-label'", Ct);
            // The epic cannot be deleted through the API while PLAN-1 references it; it is written deleted here, and waits for the issue.
            await context.Database.ExecuteSqlRawAsync("update epic set deleted_at = now() - interval '8 days', deleted_by = (select id from identity limit 1)", Ct);
            context.Idempotency.Add(IdempotencyRecord.Of((await context.Users.SingleAsync(Ct)).Id, "old", new byte[32], 201, null, Migrated.Now.AddDays(-2)));
            await context.SaveChangesAsync(Ct);
        }

        // A write in PLAN: PLAN-1 and the label go, with the comment and the history; PLAN-2 stays; OTHER-1 stays.
        await Issues(admin, new { title = "A write" });

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            Assert.Equal([2, 3], await context.Issues.Where(i => i.Title != "Elsewhere").Select(i => i.Number).OrderBy(n => n).ToListAsync(Ct));
            Assert.False(await context.Labels.AnyAsync(l => l.Name == "old-label", Ct));
            Assert.Equal(0, await context.Comments.CountAsync(Ct));
            Assert.Equal(0, await context.Idempotency.CountAsync(Ct));
            // The issue went first in the same sweep, so nothing referenced the epic any more.
            Assert.Equal(0, await context.Epics.CountAsync(Ct));
            // OTHER-1 still waits for a write in OTHER.
            var other = await context.Issues.SingleAsync(i => i.Title == "Elsewhere", Ct);
            Assert.NotNull(other.DeletedAt);
        }

        // A deleted project past its grace period goes with everything in it, on a write anywhere.
        await admin.DeleteAsync("/projects/OTHER", Ct);
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync("update project set deleted_at = now() - interval '8 days' where key = 'OTHER'", Ct);
        }

        await Issues(admin, new { title = "A third write" });
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            Assert.False(await context.Projects.AnyAsync(p => p.Key == "OTHER", Ct));
            Assert.False(await context.Issues.AnyAsync(i => i.Title == "Elsewhere", Ct));
        }

        // The key is free again.
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/projects", new { key = "OTHER", name = "other again" }, Ct)).StatusCode);
    }

    private static async Task<string[]> NextKeys(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()!)];
    }

    private static async Task<HttpClient> Project(AnInstance instance)
    {
        var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var project = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        return admin;
    }

    private static async Task Issues(HttpClient admin, params object[] issues)
    {
        using var created = await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private static async Task<HttpClient> Agent(AnInstance instance, HttpClient admin, string name)
    {
        using var created = await admin.PostAsJsonAsync("/agents", new { name }, Ct);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }
}
