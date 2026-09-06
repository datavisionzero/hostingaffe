using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// <c>next</c> over HTTP (VISION 10): the eight conditions, the order, the
/// filters, the lock, and the counts that explain an empty answer.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NextEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Wait_returns_when_an_issue_becomes_workable_and_the_deadline_returns_the_ordinary_empty_answer()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var label = await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "wanted" }, Ct);
        Assert.Equal(HttpStatusCode.Created, label.StatusCode);
        using var agent = await Agent(instance, admin, "one");

        var waiting = agent.PostAsJsonAsync("/projects/PLAN/next", new { wait = 5, label = new[] { "wanted" } }, Ct);
        await Task.Delay(200, Ct);
        Assert.False(waiting.IsCompleted);

        await Issues(admin, new { title = "Does not match" });
        await Task.Delay(200, Ct);
        Assert.False(waiting.IsCompleted);

        await Issues(admin, new { title = "Arrived while waiting", labels = new[] { "wanted" } });
        using var woken = await waiting.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(HttpStatusCode.OK, woken.StatusCode);
        var issue = (await woken.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue");
        Assert.Equal("PLAN-2", issue.GetProperty("key").GetString());
        Assert.Equal("in_progress", issue.GetProperty("status").GetString());

        var elapsed = Stopwatch.StartNew();
        using var timedOut = await agent.PostAsJsonAsync("/projects/PLAN/next", new { wait = 1, label = new[] { "wanted" } }, Ct);
        elapsed.Stop();
        Assert.Equal(HttpStatusCode.OK, timedOut.StatusCode);
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(800), $"Wait returned after {elapsed.Elapsed}.");
        Assert.Equal(JsonValueKind.Null, (await timedOut.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue").ValueKind);
    }

    [Fact]
    public async Task Wait_has_a_positive_one_hour_server_bound()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var agent = await Agent(instance, admin, "one");

        await ProjectEndpointTests.Problem(
            await agent.PostAsJsonAsync("/projects/PLAN/next", new { wait = 0 }, Ct),
            HttpStatusCode.BadRequest,
            "validation");
        await ProjectEndpointTests.Problem(
            await agent.PostAsJsonAsync("/projects/PLAN/next", new { wait = 3601 }, Ct),
            HttpStatusCode.UnprocessableEntity,
            "wait-too-long");
    }

    [Fact]
    public async Task Two_agents_asking_at_once_get_two_different_issues()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "A" }, new { title = "B" }, new { title = "C" }, new { title = "D" });
        using var one = await Agent(instance, admin, "one");
        using var two = await Agent(instance, admin, "two");

        for (var round = 0; round < 2; round++)
        {
            var answers = await Task.WhenAll(
                one.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct),
                two.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct));

            var keys = new List<string>();
            foreach (var answer in answers)
            {
                Assert.True(answer.StatusCode == HttpStatusCode.OK,
                    $"Expected OK, got {answer.StatusCode}: {await answer.Content.ReadAsStringAsync(Ct)}");
                var body = await answer.Content.ReadFromJsonAsync<JsonElement>(Ct);
                keys.Add(body.GetProperty("issue").GetProperty("key").GetString()!);
                Assert.Equal("in_progress", body.GetProperty("issue").GetProperty("status").GetString());
                answer.Dispose();
            }

            Assert.Equal(2, keys.Distinct().Count());
        }

        // All four are held now; the fifth ask gets nothing and says why.
        using var empty = await one.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct);
        var nothing = await empty.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(JsonValueKind.Null, nothing.GetProperty("issue").ValueKind);
        Assert.Equal(4, nothing.GetProperty("reasons").GetProperty("in_progress").GetInt32());
    }

    [Fact]
    public async Task An_issue_with_an_open_question_or_an_open_blocker_is_never_handed_out()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin,
            new { @ref = "blocker", title = "Blocker", status = "backlog" },
            new { title = "Blocked", blocked_by = new[] { "blocker" } },
            new { title = "Asking" },
            new { title = "Free" });
        using var agent = await Agent(instance, admin, "one");

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var asking = await context.Issues.SingleAsync(i => i.Number == 3, Ct);
            var user = await context.Users.SingleAsync(Ct);
            context.Questions.Add(Question.Ask(asking.ProjectId, asking.Id, "Which one?", user.Id, Migrated.Now));
            await context.SaveChangesAsync(Ct);
        }

        var preview = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(["PLAN-4"], preview.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        Assert.Equal(1, preview.GetProperty("total").GetInt32());

        using var first = await agent.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct);
        Assert.Equal("PLAN-4", (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue").GetProperty("key").GetString());

        using var second = await agent.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct);
        var answer = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("issue").ValueKind);
        var reasons = answer.GetProperty("reasons");
        Assert.Equal(1, reasons.GetProperty("blocked").GetInt32());
        Assert.Equal(1, reasons.GetProperty("waiting_for_answer").GetInt32());
        Assert.Equal(1, reasons.GetProperty("in_progress").GetInt32());
        Assert.Equal(1, reasons.GetProperty("parked").GetInt32());
        Assert.Equal(0, reasons.GetProperty("in_review").GetInt32());
        Assert.Equal(0, reasons.GetProperty("not_ready").GetInt32());
        Assert.Equal(0, reasons.GetProperty("assigned_elsewhere").GetInt32());

        // A blocker that closes, or is deleted, dissolves the block.
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync("update issue set status = 'canceled', closed_at = now() where number = 1", Ct);
        }

        using var third = await agent.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct);
        Assert.Equal("PLAN-2", (await third.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue").GetProperty("key").GetString());
    }

    [Fact]
    public async Task The_epic_tie_breaker_prefers_the_empty_epic_and_priority_still_trumps_it()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var project = await context.Projects.SingleAsync(Ct);
            var user = await context.Users.SingleAsync(Ct);
            context.Epics.AddRange(Epic.Create(project.Id, 1, "Busy", user.Id, Migrated.Now), Epic.Create(project.Id, 2, "Quiet", user.Id, Migrated.Now));
            await context.SaveChangesAsync(Ct);
        }

        await Issues(admin,
            new { title = "Busy 1", epic = "PLAN-E1", priority = 2 },
            new { title = "Busy 2", epic = "PLAN-E1", priority = 2 },
            new { title = "Quiet 1", epic = "PLAN-E2", priority = 2 },
            new { title = "No epic", priority = 2 },
            new { title = "Urgent in busy", epic = "PLAN-E1", priority = 4 });
        using var one = await Agent(instance, admin, "one");
        using var two = await Agent(instance, admin, "two");

        // Agent one works in the busy epic (PLAN-1 is oldest at equal priority... but PLAN-5 is urgent).
        using var first = await one.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct);
        Assert.Equal("PLAN-5", (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue").GetProperty("key").GetString());

        // Agent two: PLAN-1, PLAN-3 and PLAN-4 tie on priority; E1 is busy with
        // agent one, so the quiet epic and the epic-less issue come first, older
        // first among them.
        var preview = await two.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(["PLAN-3", "PLAN-4", "PLAN-1", "PLAN-2"], preview.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));

        // For agent one itself, the busy epic is its own: nobody *else* is in it.
        var own = await one.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(["PLAN-1", "PLAN-2", "PLAN-3", "PLAN-4"], own.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));

        // `--epic` forces a theme.
        using var forced = await two.PostAsJsonAsync("/projects/PLAN/next", new { epic = "PLAN-E1" }, Ct);
        Assert.Equal("PLAN-1", (await forced.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue").GetProperty("key").GetString());
    }

    [Fact]
    public async Task Repo_hands_out_issues_carrying_the_label_or_none_of_the_group()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "repo/api", group = "repo" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "repo/web", group = "repo" }, Ct);
        await Issues(admin,
            new { title = "Web", labels = new[] { "repo/web" } },
            new { title = "Api", labels = new[] { "repo/api" } },
            new { title = "Anywhere" });
        using var agent = await Agent(instance, admin, "one");

        var preview = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next?repo=repo%2Fapi", Ct);
        Assert.Equal(["PLAN-2", "PLAN-3"], preview.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));

        using var first = await agent.PostAsJsonAsync("/projects/PLAN/next", new { repo = "repo/api" }, Ct);
        Assert.Equal("PLAN-2", (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue").GetProperty("key").GetString());
        using var second = await agent.PostAsJsonAsync("/projects/PLAN/next", new { repo = "repo/api" }, Ct);
        Assert.Equal("PLAN-3", (await second.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue").GetProperty("key").GetString());
        using var third = await agent.PostAsJsonAsync("/projects/PLAN/next", new { repo = "repo/api" }, Ct);
        Assert.Equal(JsonValueKind.Null, (await third.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue").ValueKind);

        await ProjectEndpointTests.Problem(await agent.PostAsJsonAsync("/projects/PLAN/next", new { repo = "repo/cli" }, Ct), HttpStatusCode.UnprocessableEntity, "unknown-label");
        await ProjectEndpointTests.Problem(await agent.PostAsJsonAsync("/projects/PLAN/next", new { repo = "bug" }, Ct), HttpStatusCode.UnprocessableEntity, "unknown-label");
        await ProjectEndpointTests.Problem(await agent.GetAsync("/projects/PLAN/next?label=nope", Ct), HttpStatusCode.UnprocessableEntity, "unknown-label");
    }

    [Fact]
    public async Task Ready_triage_assignment_and_labels_narrow_the_supply_and_the_counts_say_so()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "cut-1" }, Ct);
        await Issues(admin,
            new { title = "Ready", ready = true, labels = new[] { "cut-1" } },
            new { title = "Thin" },
            new { title = "Theirs", ready = true, assignee = "maintainer" },
            new { title = "Reviewing", ready = true });
        using var agent = await Agent(instance, admin, "one");
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync("update issue set status = 'review' where number = 4", Ct);
        }

        // Triage off: `Thin` is pulled too, unless `ready` is asked for.
        var all = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(["PLAN-1", "PLAN-2"], all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        Assert.Equal(0, all.GetProperty("reasons").GetProperty("not_ready").GetInt32());
        Assert.Equal(1, all.GetProperty("reasons").GetProperty("assigned_elsewhere").GetInt32());
        Assert.Equal(1, all.GetProperty("reasons").GetProperty("in_review").GetInt32());

        var flagged = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next?ready=true", Ct);
        Assert.Equal(["PLAN-1"], flagged.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        Assert.Equal(1, flagged.GetProperty("reasons").GetProperty("not_ready").GetInt32());

        // Triage on: `ready` is binding.
        await admin.PatchAsJsonAsync("/projects/PLAN", new { triage_required = true }, Ct);
        var triaged = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(["PLAN-1"], triaged.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));

        // The label filter, and the assignee's own supply.
        var labelled = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next?label=cut-1", Ct);
        Assert.Equal(1, labelled.GetProperty("total").GetInt32());
        var mine = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(["PLAN-1", "PLAN-3"], mine.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));

        // The act writes the ordinary claim, history included.
        using var taken = await agent.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct);
        var issue = (await taken.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue");
        Assert.Equal("PLAN-1", issue.GetProperty("key").GetString());
        Assert.Equal("one", issue.GetProperty("claim").GetProperty("holder").GetProperty("name").GetString());
        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        Assert.Equal(1, await reader.History.CountAsync(h => h.Field == "claim", Ct));
    }

    /// <summary>
    /// The window between the candidate query's snapshot and the lock it takes
    /// on the row it picked. Somebody claiming that row inside the window used
    /// to come back as `claim-held`: `for update` rechecks a row another writer
    /// changed, but the recheck only sees the conditions naming the locked
    /// table, and they all named the CTE. `next` refuses nothing a caller can
    /// act on (VISION 11) — it hands out the next issue instead.
    /// </summary>
    /// <remarks>
    /// The rows are there to widen the window, not because the count matters:
    /// the query has to take long enough for the claim beside it to commit
    /// while it runs. Without the recheck this fails on nearly every attempt.
    /// </remarks>
    [Fact]
    public async Task An_issue_claimed_while_next_is_choosing_is_not_the_one_next_hands_out()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        for (var batch = 0; batch < 6; batch++)
        {
            await Issues(admin, [.. Enumerable.Range(0, 100).Select(n => new { title = $"Work {batch}-{n}" })]);
        }

        using var agent = await Agent(instance, admin, "one");
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var preview = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next?limit=1", Ct);
            var top = preview.GetProperty("items")[0].GetProperty("key").GetString()!;

            var asking = agent.PostAsJsonAsync("/projects/PLAN/next", new { }, Ct);
            await Task.Delay(attempt, Ct);
            using var stolen = await admin.PostAsJsonAsync($"/issues/{top}/claim", new { }, Ct);
            Assert.Equal(HttpStatusCode.OK, stolen.StatusCode);

            using var answer = await asking;
            Assert.True(answer.StatusCode == HttpStatusCode.OK,
                $"Attempt {attempt}: expected OK, got {answer.StatusCode}: {await answer.Content.ReadAsStringAsync(Ct)}");

            var handed = (await answer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issue");
            Assert.NotEqual(top, handed.GetProperty("key").GetString());
            Assert.Equal("in_progress", handed.GetProperty("status").GetString());
        }
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
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }
}
