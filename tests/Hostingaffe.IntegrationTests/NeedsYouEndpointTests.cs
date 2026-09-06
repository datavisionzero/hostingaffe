using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.IntegrationTests;

/// <summary>The human work list of VISION 10, including its recursive blocker rule.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class NeedsYouEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_four_groups_are_ordered_deduplicated_and_paginated()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe", triage_required = true }, Ct);
        await admin.PostAsJsonAsync("/agents", new { name = "worker" }, Ct);

        using var created = await admin.PostAsJsonAsync("/issues", new
        {
            project = "PLAN",
            issues = new object[]
            {
                new { title = "Question and review", priority = 0 },
                new { title = "Review", priority = 4 },
                new { title = "Unready", priority = 3 },
                new { title = "Stuck through a chain", priority = 2, ready = true, blocked_by = new[] { "middle" } },
                new { @ref = "parked", title = "Parked leaf", status = "backlog" },
                new { title = "Blocked but an agent can resolve it", ready = true, blocked_by = new[] { "workable" } },
                new { @ref = "workable", title = "Workable", ready = true },
                new { @ref = "middle", title = "Middle", ready = true, blocked_by = new[] { "parked" } },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var user = await context.Users.SingleAsync(Ct);
            var question = await context.Issues.SingleAsync(issue => issue.Number == 1, Ct);
            context.Questions.Add(Question.Ask(question.ProjectId, question.Id, "Which way?", user.Id, Migrated.Now));
            await context.Database.ExecuteSqlRawAsync("update issue set status = 'review' where number in (1, 2)", Ct);
            await context.SaveChangesAsync(Ct);
        }

        var first = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/needs-you?limit=2", Ct);
        Assert.Equal(5, first.GetProperty("total").GetInt32());
        Assert.Equal(1, first.GetProperty("agents").GetInt32());
        Assert.True(first.GetProperty("has_more").GetBoolean());
        Assert.Equal(
            [("PLAN-1", "question"), ("PLAN-2", "review")],
            Entries(first));

        var cursor = Uri.EscapeDataString(first.GetProperty("next_cursor").GetString()!);
        var second = await admin.GetFromJsonAsync<JsonElement>($"/projects/PLAN/needs-you?limit=2&cursor={cursor}", Ct);
        Assert.Equal([("PLAN-3", "unready"), ("PLAN-4", "stuck")], Entries(second));

        cursor = Uri.EscapeDataString(second.GetProperty("next_cursor").GetString()!);
        var third = await admin.GetFromJsonAsync<JsonElement>($"/projects/PLAN/needs-you?limit=2&cursor={cursor}", Ct);
        Assert.Equal([("PLAN-8", "stuck")], Entries(third));
        Assert.False(third.GetProperty("has_more").GetBoolean());

        await ProjectEndpointTests.Problem(
            await admin.GetAsync("/projects/PLAN/needs-you?cursor=not-a-cursor", Ct),
            HttpStatusCode.BadRequest,
            "cursor-invalid");

        await ProjectEndpointTests.Problem(
            await admin.GetAsync($"/projects/NOPE/needs-you?cursor={Uri.EscapeDataString(first.GetProperty("next_cursor").GetString()!)}", Ct),
            HttpStatusCode.NotFound,
            "not-found");
    }

    // PLAN-29, both halves. An instance without an agent used to make every
    // blocked issue stuck at once — a fact about the instance turned into a
    // property of tickets — and a parked issue stayed on the list although
    // parking is the explicit decision that it is not up yet.
    [Fact]
    public async Task A_missing_agent_is_said_once_and_a_parked_issue_is_not_stuck_itself()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await admin.PostAsJsonAsync("/agents", new { name = "worker" }, Ct);
        using var created = await admin.PostAsJsonAsync("/issues", new
        {
            project = "PLAN",
            issues = new object[]
            {
                new { @ref = "leaf", title = "Parked leaf", status = "backlog" },
                new { title = "Waiting behind the parked one", ready = true, blocked_by = new[] { "leaf" } },
                new { title = "Parked and blocked", status = "backlog", blocked_by = new[] { "leaf" } },
                new { title = "Parked, blocked and asked", status = "backlog", blocked_by = new[] { "leaf" } },
                new { title = "Unready" },
                new { title = "Blocked but an agent can resolve it", ready = true, blocked_by = new[] { "free" } },
                new { @ref = "free", title = "Free", ready = true },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var user = await context.Users.SingleAsync(Ct);
            var asked = await context.Issues.SingleAsync(issue => issue.Number == 4, Ct);
            context.Questions.Add(Question.Ask(asked.ProjectId, asked.Id, "Still?", user.Id, Migrated.Now));
            await context.SaveChangesAsync(Ct);
        }

        // PLAN-4 is parked and carries a question, so it is on the list as a
        // question: parking says nothing about an answer being owed. PLAN-2
        // waits behind a parked blocker, which is the real find. PLAN-3 is
        // parked itself and is not. `unready` is absent without triage.
        var withAgent = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/needs-you", Ct);
        Assert.Equal([("PLAN-4", "question"), ("PLAN-2", "stuck")], Entries(withAgent));
        Assert.Equal(1, withAgent.GetProperty("agents").GetInt32());

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync("update token set revoked_at = now() where kind = 'agent'", Ct);
        }

        // The instance losing its last agent changes the list not at all. It
        // changes the one number beside it, and that is the whole message.
        var withoutAgent = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/needs-you", Ct);
        Assert.Equal([("PLAN-4", "question"), ("PLAN-2", "stuck")], Entries(withoutAgent));
        Assert.Equal(0, withoutAgent.GetProperty("agents").GetInt32());

        // Unparking puts the issue back on the list as stuck: nothing about the
        // chain changed, only the decision that it is not up yet.
        using var unparked = await admin.PatchAsJsonAsync("/issues/PLAN-3", new { status = "todo" }, Ct);
        Assert.Equal(HttpStatusCode.OK, unparked.StatusCode);
        var after = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/needs-you", Ct);
        Assert.Equal([("PLAN-4", "question"), ("PLAN-2", "stuck"), ("PLAN-3", "stuck")], Entries(after));
    }

    [Fact]
    public async Task Wait_uses_the_page_etag_and_wakes_when_the_list_changes()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new[] { new { title = "A" } } }, Ct);

        using var initial = await admin.GetAsync("/projects/PLAN/needs-you", Ct);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var etag = initial.Headers.ETag?.ToString();
        Assert.NotNull(etag);
        Assert.Empty(Entries(await initial.Content.ReadFromJsonAsync<JsonElement>(Ct)));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/projects/PLAN/needs-you?wait=5");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var waiting = admin.SendAsync(request, Ct);
        await Task.Delay(100, Ct);
        using var asked = await admin.PostAsJsonAsync("/issues/PLAN-1/questions", new { question = "Which way?" }, Ct);

        using var changed = await waiting.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal([("PLAN-1", "question")], Entries(await changed.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        var changedTag = changed.Headers.ETag?.ToString();
        Assert.NotNull(changedTag);
        Assert.NotEqual(etag, changedTag);

        using var unchangedRequest = new HttpRequestMessage(HttpMethod.Get, "/projects/PLAN/needs-you?wait=1");
        unchangedRequest.Headers.TryAddWithoutValidation("If-None-Match", changedTag);
        var started = DateTimeOffset.UtcNow;
        using var unchanged = await admin.SendAsync(unchangedRequest, Ct);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(750));
        Assert.Equal(changedTag, unchanged.Headers.ETag?.ToString());

        await ProjectEndpointTests.Problem(await admin.GetAsync("/projects/PLAN/needs-you?wait=0", Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(await admin.GetAsync("/projects/PLAN/needs-you?wait=3601", Ct), HttpStatusCode.UnprocessableEntity, "wait-too-long");
    }

    private static (string Key, string Because)[] Entries(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => (
            item.GetProperty("issue").GetProperty("key").GetString()!,
            item.GetProperty("because").GetString()!))];
}
