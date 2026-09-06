using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hostingaffe.IntegrationTests;

/// <summary>Epics over HTTP (VISION 7, <c>docs/api.md</c>): a bracket whose status gates nothing.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class EpicEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Progress_counts_what_the_vision_says_and_closing_leaves_the_issues_workable()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var agent = await Agent(instance, admin, "one");

        using var created = await agent.PostAsJsonAsync("/epics", new { project = "PLAN", title = "Backend", description = "The plan.", labels = new[] { "feature" } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var epic = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("PLAN-E1", epic.GetProperty("key").GetString());
        Assert.Equal("one", epic.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("feature", Assert.Single(epic.GetProperty("labels").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(0, epic.GetProperty("progress").GetProperty("total").GetInt32());

        await Issues(admin,
            new { title = "A", epic = "PLAN-E1" }, new { title = "B", epic = "PLAN-E1" }, new { title = "C", epic = "PLAN-E1" },
            new { title = "D", epic = "PLAN-E1" }, new { title = "E", epic = "PLAN-E1" });
        await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done" }, Ct);
        await admin.PostAsJsonAsync("/issues/PLAN-2/close", new { status = "done" }, Ct);
        await admin.PostAsJsonAsync("/issues/PLAN-3/close", new { status = "canceled" }, Ct);
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var user = await context.Users.SingleAsync(Ct);
            await context.Database.ExecuteSqlRawAsync("update issue set deleted_at = now(), deleted_by = {0} where number = 4", [user.Id], Ct);
        }

        // 5 attached, one deleted: total 4, closed 3 (2 done, 1 canceled), 1 open.
        var read = await admin.GetFromJsonAsync<JsonElement>("/epics/PLAN-E1", Ct);
        var progress = read.GetProperty("progress");
        Assert.Equal(4, progress.GetProperty("total").GetInt32());
        Assert.Equal(3, progress.GetProperty("closed").GetInt32());
        Assert.Equal(2, progress.GetProperty("done").GetInt32());
        Assert.Equal(1, progress.GetProperty("canceled").GetInt32());

        // Closing gates nothing: PLAN-5 is still handed out, and the answer carries the progress.
        using var closed = await admin.PostAsync("/epics/PLAN-E1/close", null, Ct);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var closedEpic = await closed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("closed", closedEpic.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, closedEpic.GetProperty("closed_at").ValueKind);
        Assert.Equal(4, closedEpic.GetProperty("progress").GetProperty("total").GetInt32());

        var next = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(["PLAN-5"], next.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        await ProjectEndpointTests.Problem(await admin.PostAsync("/epics/PLAN-E1/close", null, Ct), HttpStatusCode.UnprocessableEntity, "transition");

        // Attaching an issue to the closed epic reopens it.
        await Issues(admin, new { title = "F" });
        await admin.PatchAsJsonAsync("/issues/PLAN-6", new { epic = "PLAN-E1" }, Ct);
        Assert.Equal("open", (await admin.GetFromJsonAsync<JsonElement>("/epics/PLAN-E1", Ct)).GetProperty("status").GetString());
        await ProjectEndpointTests.Problem(await admin.PostAsync("/epics/PLAN-E1/reopen", null, Ct), HttpStatusCode.UnprocessableEntity, "transition");
    }

    [Fact]
    public async Task The_living_document_is_guarded_and_its_change_is_history()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "cut-1" }, Ct);
        using var created = await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = "Backend", description = "v1" }, Ct);
        var epic = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var version = epic.GetProperty("updated_at").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/epics/PLAN-E1")
        {
            Content = JsonContent.Create(new { title = "Backend and data model", description = "v2", labels = new[] { "cut-1" } }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var changed = await admin.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("v2", after.GetProperty("description").GetString());
        Assert.Equal("cut-1", Assert.Single(after.GetProperty("labels").EnumerateArray()).GetProperty("name").GetString());

        using var staleRequest = new HttpRequestMessage(HttpMethod.Patch, "/epics/PLAN-E1") { Content = JsonContent.Create(new { description = "v3" }) };
        staleRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var problem = await ProjectEndpointTests.Problem(await admin.SendAsync(staleRequest, Ct), HttpStatusCode.PreconditionFailed, "stale");
        Assert.Equal("v2", problem.GetProperty("current").GetProperty("description").GetString());

        // Without the header the write goes through, as everywhere: the guard is
        // offered, never imposed (`docs/api.md`, Concurrency on text fields).
        using var unguarded = await admin.PatchAsJsonAsync("/epics/PLAN-E1", new { description = "v3" }, Ct);
        Assert.Equal(HttpStatusCode.OK, unguarded.StatusCode);
        Assert.Equal("v3", (await unguarded.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("description").GetString());

        await ProjectEndpointTests.Problem(await admin.PatchAsJsonAsync("/epics/PLAN-E1", new { labels = new[] { "bug", "feature" } }, Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(await admin.GetAsync("/epics/PLAN-E9", Ct), HttpStatusCode.NotFound, "not-found");

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        var fields = await reader.History.Where(h => h.EpicId != null).OrderBy(h => h.Id).Select(h => h.Field).ToListAsync(Ct);
        Assert.Equal(["created", "title", "description", "label", "description"], fields);
        // The history records that the document changed, not how.
        Assert.All(await reader.History.Where(h => h.Field == "description").ToListAsync(Ct), entry => Assert.Null(entry.NewValue));
    }

    [Fact]
    public async Task Epics_list_newest_first_by_status_and_label_and_page()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        for (var i = 1; i <= 4; i++)
        {
            await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = $"E{i}", labels = i % 2 == 0 ? new[] { "feature" } : Array.Empty<string>() }, Ct);
        }

        await admin.PostAsync("/epics/PLAN-E2/close", null, Ct);

        var open = await admin.GetFromJsonAsync<JsonElement>("/epics?project=PLAN", Ct);
        Assert.Equal(["PLAN-E4", "PLAN-E3", "PLAN-E1"], open.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("key").GetString()));
        Assert.Equal(["PLAN-E2"], (await admin.GetFromJsonAsync<JsonElement>("/epics?project=PLAN&status=closed", Ct)).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("key").GetString()));
        Assert.Equal(4, (await admin.GetFromJsonAsync<JsonElement>("/epics?project=PLAN&status=all", Ct)).GetProperty("total").GetInt32());
        Assert.Equal(["PLAN-E4"], (await admin.GetFromJsonAsync<JsonElement>("/epics?project=PLAN&label=feature", Ct)).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("key").GetString()));

        var first = await admin.GetFromJsonAsync<JsonElement>("/epics?project=PLAN&status=all&limit=3", Ct);
        Assert.True(first.GetProperty("has_more").GetBoolean());
        var second = await admin.GetFromJsonAsync<JsonElement>($"/epics?project=PLAN&status=all&limit=3&cursor={Uri.EscapeDataString(first.GetProperty("next_cursor").GetString()!)}", Ct);
        Assert.Equal(["PLAN-E1"], second.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("key").GetString()));
        Assert.Equal(["feature"], first.GetProperty("items")[0].GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task Deleting_is_refused_while_issues_reference_the_epic_and_restoring_brings_it_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = "Backend" }, Ct);
        await Issues(admin, new { title = "A", epic = "PLAN-E1" }, new { title = "B", epic = "PLAN-E1" });

        var problem = await ProjectEndpointTests.Problem(await admin.DeleteAsync("/epics/PLAN-E1", Ct), HttpStatusCode.UnprocessableEntity, "has-issues");
        Assert.Equal(2, problem.GetProperty("count").GetInt32());

        // A deleted issue still references it.
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var user = await context.Users.SingleAsync(Ct);
            await context.Database.ExecuteSqlRawAsync("update issue set deleted_at = now(), deleted_by = {0} where number = 1", [user.Id], Ct);
        }

        await admin.PatchAsJsonAsync("/issues/PLAN-2", new { epic = (string?)null }, Ct);
        Assert.Equal(1, (await ProjectEndpointTests.Problem(await admin.DeleteAsync("/epics/PLAN-E1", Ct), HttpStatusCode.UnprocessableEntity, "has-issues")).GetProperty("count").GetInt32());

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync("update issue set epic_id = null where number = 1", Ct);
        }

        using var deleted = await admin.DeleteAsync("/epics/PLAN-E1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var gone = await ProjectEndpointTests.Problem(await admin.GetAsync("/epics/PLAN-E1", Ct), HttpStatusCode.NotFound, "deleted");
        Assert.True(gone.TryGetProperty("restorable_until", out _));
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>("/epics?project=PLAN&status=all", Ct)).GetProperty("total").GetInt32());
        await ProjectEndpointTests.Problem(await admin.PatchAsJsonAsync("/issues/PLAN-2", new { epic = "PLAN-E1" }, Ct), HttpStatusCode.BadRequest, "validation");

        using var restored = await admin.PostAsync("/epics/PLAN-E1/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/epics/PLAN-E1", Ct)).StatusCode);
        await ProjectEndpointTests.Problem(await admin.PostAsync("/epics/PLAN-E1/restore", null, Ct), HttpStatusCode.UnprocessableEntity, "transition");
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
