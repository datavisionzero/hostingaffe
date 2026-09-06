using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.IntegrationTests;

/// <summary>Labels over HTTP (<c>docs/api.md</c>, Labels), the group rule included.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class LabelEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_label_is_created_changed_deleted_and_restored()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);

        using var created = await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "area:infra", description = "Compose, CI, the image." }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var label = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(JsonValueKind.Null, label.GetProperty("group").ValueKind);

        // Into a group; description cleared with an explicit null; name kept.
        using var changed = await admin.PatchAsJsonAsync("/projects/PLAN/labels/area:infra", new { group = "area", description = (string?)null }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("area", after.GetProperty("group").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("description").ValueKind);
        Assert.Equal("area:infra", after.GetProperty("name").GetString());

        // Renamed, then gone, then back.
        using var renamed = await admin.PatchAsJsonAsync("/projects/PLAN/labels/area:infra", new { name = "area:ops" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        using var deleted = await admin.DeleteAsync("/projects/PLAN/labels/area:ops", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var listed = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/labels", Ct);
        Assert.DoesNotContain(listed.EnumerateArray(), l => l.GetProperty("name").GetString() == "area:ops");
        // A deleted label is not found by the acts on a live one; restore is the one that sees it.
        await ProjectEndpointTests.Problem(await admin.PatchAsJsonAsync("/projects/PLAN/labels/area:ops", new { name = "x" }, Ct), HttpStatusCode.NotFound, "not-found");

        // The name stays taken while the label waits for a restore.
        await ProjectEndpointTests.Problem(await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "area:ops" }, Ct), HttpStatusCode.BadRequest, "validation");

        using var restored = await admin.PostAsync("/projects/PLAN/labels/area:ops/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        listed = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/labels", Ct);
        Assert.Contains(listed.EnumerateArray(), l => l.GetProperty("name").GetString() == "area:ops");
    }

    [Fact]
    public async Task A_name_follows_the_pattern_and_is_unique_in_the_project()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);

        await ProjectEndpointTests.Problem(await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "Bug" }, Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "bug" }, Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "ok", group = "Kind" }, Ct), HttpStatusCode.BadRequest, "validation");

        // A slash is part of the pattern, and reaches the route encoded.
        using var created = await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "repo/hostingaffe", group = "repo" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var deleted = await admin.DeleteAsync($"/projects/PLAN/labels/{Uri.EscapeDataString("repo/hostingaffe")}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Agents_create_labels_too()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        using var createdAgent = await admin.PostAsJsonAsync("/agents", new { }, Ct);
        using var agent = instance.ClientWith((await createdAgent.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());

        using var created = await agent.PostAsJsonAsync("/projects/PLAN/labels", new { name = "cut-1" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task A_group_change_that_would_leave_an_issue_with_two_of_a_group_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "urgent-ish" }, Ct);

        // No issue endpoint yet: PLAN-1 carries `bug` (kind) and `urgent-ish`,
        // PLAN-2 carries `urgent-ish` alone, written straight into the tables.
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var project = await context.Projects.SingleAsync(Ct);
            var author = await context.Users.SingleAsync(Ct);
            var bug = await context.Labels.SingleAsync(l => l.Name == "bug", Ct);
            var urgentIsh = await context.Labels.SingleAsync(l => l.Name == "urgent-ish", Ct);
            var one = Issue.Create(project.Id, 1, "One", author.Id, Migrated.Now);
            var two = Issue.Create(project.Id, 2, "Two", author.Id, Migrated.Now);
            context.AddRange(one, two,
                IssueLabel.Attach(one.Id, bug.Id), IssueLabel.Attach(one.Id, urgentIsh.Id),
                IssueLabel.Attach(two.Id, urgentIsh.Id));
            await context.SaveChangesAsync(Ct);
        }

        using var refused = await admin.PatchAsJsonAsync("/projects/PLAN/labels/urgent-ish", new { group = "kind" }, Ct);
        var problem = await ProjectEndpointTests.Problem(refused, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(["PLAN-1"], problem.GetProperty("issues").EnumerateArray().Select(i => i.GetString()));
        Assert.Empty(problem.GetProperty("epics").EnumerateArray());
        Assert.True(problem.GetProperty("errors").TryGetProperty("group", out _));

        // Into a group nothing else on those issues carries: fine.
        using var moved = await admin.PatchAsJsonAsync("/projects/PLAN/labels/urgent-ish", new { group = "priority-ish" }, Ct);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
    }

    /// <summary>
    /// The group rule is the epic's too, and a group change has to ask the
    /// epics as well — the structured roadmap of VISION 7 is a label group on
    /// epics, and nothing else keeps one of them off two of a group.
    /// </summary>
    [Fact]
    public async Task A_group_change_that_would_leave_an_epic_with_two_of_a_group_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "next", group = "horizon" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "urgent-ish" }, Ct);

        // Two of one group in the same set is refused on an epic as on an issue.
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "later", group = "horizon" }, Ct);
        await ProjectEndpointTests.Problem(
            await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = "Both", labels = new[] { "next", "later" } }, Ct),
            HttpStatusCode.BadRequest,
            "validation");

        // PLAN-E1 carries `next` (horizon) and the ungrouped `urgent-ish`.
        using var created = await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = "The roadmap", labels = new[] { "next", "urgent-ish" } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Moving `urgent-ish` into `horizon` would leave the epic with two of it.
        using var refused = await admin.PatchAsJsonAsync("/projects/PLAN/labels/urgent-ish", new { group = "horizon" }, Ct);
        var problem = await ProjectEndpointTests.Problem(refused, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(["PLAN-E1"], problem.GetProperty("epics").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(problem.GetProperty("issues").EnumerateArray());
        Assert.True(problem.GetProperty("errors").TryGetProperty("group", out _));

        // Into a group the epic carries nothing else of: fine.
        using var moved = await admin.PatchAsJsonAsync("/projects/PLAN/labels/urgent-ish", new { group = "priority-ish" }, Ct);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
    }
}
