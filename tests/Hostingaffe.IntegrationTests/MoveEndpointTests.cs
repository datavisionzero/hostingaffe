using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The transition table of <c>docs/api.md</c>, cell by cell (ADR 0016), and
/// where a close lands by the caller's kind and the switch (ADR 0014).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MoveEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly string[] Acts = ["claim", "release", "close", "review", "reopen", "park", "unpark"];

    /// <summary>
    /// Row: where the issue is; columns: claim, release, close, review, reopen,
    /// status backlog, status todo — as a user, review not required. `ok` is
    /// 200, `no` is `transition`.
    /// </summary>
    public static TheoryData<string, string[]> Table() => new()
    {
        { "backlog", ["ok", "no", "ok", "ok", "no", "no", "ok"] },
        { "todo", ["ok", "no", "ok", "ok", "no", "ok", "no"] },
        { "in_progress", ["ok", "ok", "ok", "ok", "no", "no", "no"] },
        { "review", ["no", "no", "ok", "no", "ok", "no", "no"] },
        { "done", ["no", "no", "no", "no", "ok", "no", "no"] },
        { "canceled", ["no", "no", "no", "no", "ok", "no", "no"] },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public async Task Every_cell_of_the_table(string from, string[] expected)
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);

        for (var column = 0; column < Acts.Length; column++)
        {
            var key = await IssueIn(admin, from, column + 1);
            using var response = await Act(admin, key, Acts[column]);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

            if (expected[column] == "ok")
            {
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{from} × {Acts[column]}: expected 200, got {response.StatusCode} {body}");
            }
            else
            {
                Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{from} × {Acts[column]}: expected 422, got {response.StatusCode}");
                Assert.Equal("/problems/transition", body.GetProperty("type").GetString());
            }
        }
    }

    [Fact]
    public async Task An_agents_close_lands_in_review_with_the_switch_on_and_in_done_with_it_off_and_a_users_in_done_either_way()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var agent = await Agent(instance, admin, "one");
        await Issues(admin, new { title = "A" }, new { title = "B" }, new { title = "C" }, new { title = "D" }, new { title = "E" });

        // Switch off: the agent's word closes.
        using var offDone = await agent.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done", result = "Shipped." }, Ct);
        var closed = await offDone.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("done", closed.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("closed_at").ValueKind);
        Assert.Equal("Shipped.", closed.GetProperty("result").GetString());

        // Switch on: every close by an agent lands in review, canceled included, result kept, no closed_at.
        await admin.PatchAsJsonAsync("/projects/PLAN", new { review_required = true }, Ct);
        using var onDone = await agent.PostAsJsonAsync("/issues/PLAN-2/close", new { status = "done", result = "I think it works." }, Ct);
        var reviewing = await onDone.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("review", reviewing.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reviewing.GetProperty("closed_at").ValueKind);
        Assert.Equal("I think it works.", reviewing.GetProperty("result").GetString());

        using var onCanceled = await agent.PostAsJsonAsync("/issues/PLAN-3/close", new { status = "canceled", result = "Could not be done." }, Ct);
        Assert.Equal("review", (await onCanceled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());

        // From review, an agent's close is refused where review is required; a user's lands where it says.
        await ProjectEndpointTests.Problem(await agent.PostAsJsonAsync("/issues/PLAN-2/close", new { status = "done" }, Ct), HttpStatusCode.UnprocessableEntity, "transition");
        using var accepted = await admin.PostAsJsonAsync("/issues/PLAN-2/close", new { status = "done" }, Ct);
        var done = await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("done", done.GetProperty("status").GetString());
        Assert.Equal("I think it works.", done.GetProperty("result").GetString());
        using var rejectedAsCanceled = await admin.PostAsJsonAsync("/issues/PLAN-3/close", new { status = "canceled" }, Ct);
        Assert.Equal("canceled", (await rejectedAsCanceled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());

        // A user's close goes to done either way, and a close needs a status.
        using var byUser = await admin.PostAsJsonAsync("/issues/PLAN-4/close", new { status = "done" }, Ct);
        Assert.Equal("done", (await byUser.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        await ProjectEndpointTests.Problem(await admin.PostAsJsonAsync("/issues/PLAN-5/close", new { }, Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(await admin.PostAsJsonAsync("/issues/PLAN-5/close", new { status = "todo" }, Ct), HttpStatusCode.BadRequest, "validation");

        // Switch off again: an agent's close out of review goes through.
        await admin.PatchAsJsonAsync("/projects/PLAN", new { review_required = false }, Ct);
        await agent.PostAsJsonAsync("/issues/PLAN-5/review", new { result = "Have a look." }, Ct);
        using var throughReview = await agent.PostAsJsonAsync("/issues/PLAN-5/close", new { status = "done" }, Ct);
        Assert.Equal("done", (await throughReview.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Closing_and_handing_in_release_the_claim_and_reopening_keeps_the_result_and_writes_the_comment()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var agent = await Agent(instance, admin, "one");
        await Issues(admin, new { title = "A" }, new { title = "B" });

        await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);
        using var handedIn = await agent.PostAsJsonAsync("/issues/PLAN-1/review", new { result = "Done, I think." }, Ct);
        var review = await handedIn.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("review", review.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("claim").ValueKind);

        // Rejected: back to todo with a comment, result kept, no claim.
        using var reopened = await admin.PostAsJsonAsync("/issues/PLAN-1/reopen", new { comment = "The tests are missing." }, Ct);
        var todo = await reopened.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("todo", todo.GetProperty("status").GetString());
        Assert.Equal("Done, I think.", todo.GetProperty("result").GetString());
        Assert.Equal(JsonValueKind.Null, todo.GetProperty("closed_at").ValueKind);
        Assert.Equal("The tests are missing.", Assert.Single(todo.GetProperty("comments").EnumerateArray()).GetProperty("body").GetString());
        Assert.Equal("maintainer", todo.GetProperty("comments")[0].GetProperty("author").GetProperty("name").GetString());

        // Closing a claimed issue clears the claim; the next close overwrites the result; reopening a closed one clears closed_at.
        await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);
        using var closed = await agent.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done", result = "Now with tests." }, Ct);
        var done = await closed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(JsonValueKind.Null, done.GetProperty("claim").ValueKind);
        Assert.Equal("Now with tests.", done.GetProperty("result").GetString());
        using var again = await admin.PostAsJsonAsync("/issues/PLAN-1/reopen", new { }, Ct);
        Assert.Equal(JsonValueKind.Null, (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("closed_at").ValueKind);

        // Omitting the result keeps what is there.
        using var kept = await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "canceled" }, Ct);
        Assert.Equal("Now with tests.", (await kept.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("result").GetString());

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        var statuses = await reader.History.Where(h => h.Field == "status").OrderBy(h => h.Id).Select(h => h.OldValue + ">" + h.NewValue).ToListAsync(Ct);
        Assert.Equal(["todo>in_progress", "in_progress>review", "review>todo", "todo>in_progress", "in_progress>done", "done>todo", "todo>canceled"], statuses);
    }

    [Fact]
    public async Task A_user_acts_over_an_agents_claim_and_an_agent_over_nobodys()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var one = await Agent(instance, admin, "one");
        using var two = await Agent(instance, admin, "two");
        await Issues(admin, new { title = "A" }, new { title = "B" }, new { title = "C" });

        await one.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);
        await one.PostAsJsonAsync("/issues/PLAN-2/claim", new { }, Ct);
        await one.PostAsJsonAsync("/issues/PLAN-3/claim", new { }, Ct);

        var held = await ProjectEndpointTests.Problem(await two.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done" }, Ct), HttpStatusCode.Conflict, "claim-held");
        Assert.Equal("one", held.GetProperty("holder").GetProperty("name").GetString());
        await ProjectEndpointTests.Problem(await two.PostAsJsonAsync("/issues/PLAN-1/review", new { }, Ct), HttpStatusCode.Conflict, "claim-held");
        await ProjectEndpointTests.Problem(await two.PatchAsJsonAsync("/issues/PLAN-1", new { status = "backlog" }, Ct), HttpStatusCode.Conflict, "claim-held");

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/issues/PLAN-2/review", new { }, Ct)).StatusCode);

        // Parking a claimed issue is a transition even for the user, who may act
        // over the claim but not park what somebody is working on.
        await ProjectEndpointTests.Problem(await admin.PatchAsJsonAsync("/issues/PLAN-3", new { status = "backlog" }, Ct), HttpStatusCode.UnprocessableEntity, "transition");
    }

    [Fact]
    public async Task Parking_is_a_field_write_and_lands_back_in_todo()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "A" });

        using var parked = await admin.PatchAsJsonAsync("/issues/PLAN-1", new { status = "backlog", title = "Parked and renamed" }, Ct);
        var backlog = await parked.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("backlog", backlog.GetProperty("status").GetString());
        Assert.Equal("Parked and renamed", backlog.GetProperty("title").GetString());

        using var unparked = await admin.PatchAsJsonAsync("/issues/PLAN-1", new { status = "todo" }, Ct);
        Assert.Equal("todo", (await unparked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());

        await ProjectEndpointTests.Problem(await admin.PatchAsJsonAsync("/issues/PLAN-1", new { status = "done" }, Ct), HttpStatusCode.UnprocessableEntity, "transition");
    }

    private static async Task<string> IssueIn(HttpClient admin, string from, int number)
    {
        var key = $"PLAN-{number}";
        await Issues(admin, new { title = $"{from} {number}" });

        switch (from)
        {
            case "backlog":
                await admin.PatchAsJsonAsync($"/issues/{key}", new { status = "backlog" }, Ct);
                break;
            case "in_progress":
                await admin.PostAsJsonAsync($"/issues/{key}/claim", new { }, Ct);
                break;
            case "review":
                await admin.PostAsJsonAsync($"/issues/{key}/review", new { }, Ct);
                break;
            case "done":
            case "canceled":
                await admin.PostAsJsonAsync($"/issues/{key}/close", new { status = from }, Ct);
                break;
        }

        var read = await admin.GetFromJsonAsync<JsonElement>($"/issues/{key}", Ct);
        Assert.Equal(from, read.GetProperty("status").GetString());
        return key;
    }

    private static Task<HttpResponseMessage> Act(HttpClient client, string key, string act) => act switch
    {
        "claim" => client.PostAsJsonAsync($"/issues/{key}/claim", new { }, Ct),
        "release" => client.PostAsync($"/issues/{key}/release", null, Ct),
        "close" => client.PostAsJsonAsync($"/issues/{key}/close", new { status = "done" }, Ct),
        "review" => client.PostAsJsonAsync($"/issues/{key}/review", new { }, Ct),
        "reopen" => client.PostAsJsonAsync($"/issues/{key}/reopen", new { }, Ct),
        "park" => client.PatchAsJsonAsync($"/issues/{key}", new { status = "backlog" }, Ct),
        "unpark" => client.PatchAsJsonAsync($"/issues/{key}", new { status = "todo" }, Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

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
