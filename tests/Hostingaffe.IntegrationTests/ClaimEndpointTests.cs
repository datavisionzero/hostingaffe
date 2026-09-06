using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The claim over HTTP (VISION 11, <c>docs/api.md</c>): the act the product
/// exists for, so these are the point. Expiry is not waited for — the tests move
/// <c>claim_expires_at</c> in the row, which is exactly what the view reads.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ClaimEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_concurrent_claims_produce_exactly_one_winner()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Seeded(instance);
        using var one = await AgentAsync(instance, admin, "quiet-otter-42");
        using var two = await AgentAsync(instance, admin, "brisk-heron-7");

        for (var round = 1; round <= 5; round++)
        {
            var responses = await Task.WhenAll(
                one.PostAsJsonAsync($"/issues/PLAN-{round}/claim", new { }, Ct),
                two.PostAsJsonAsync($"/issues/PLAN-{round}/claim", new { }, Ct));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            var problem = await loser.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("/problems/claim-held", problem.GetProperty("type").GetString());
            Assert.Contains(problem.GetProperty("holder").GetProperty("name").GetString(), new[] { "quiet-otter-42", "brisk-heron-7" });

            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_claim_sets_in_progress_with_the_expiry_by_the_holders_kind_and_the_holder_extends()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Seeded(instance);
        using var agent = await AgentAsync(instance, admin, "quiet-otter-42");

        using var claimed = await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
        var issue = await claimed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("in_progress", issue.GetProperty("status").GetString());
        var claim = issue.GetProperty("claim");
        Assert.Equal("quiet-otter-42", claim.GetProperty("holder").GetProperty("name").GetString());
        var since = DateTimeOffset.Parse(claim.GetProperty("since").GetString()!);
        Assert.Equal(since.AddHours(4), DateTimeOffset.Parse(claim.GetProperty("expires_at").GetString()!));

        // A user's claim carries no expiry.
        using var byUser = await admin.PostAsJsonAsync("/issues/PLAN-2/claim", new { }, Ct);
        Assert.Equal(JsonValueKind.Null, (await byUser.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("claim").GetProperty("expires_at").ValueKind);

        // The holder's own claim again extends; a user's edit does not; the holder's edit does.
        await using var context = Migrated.ContextFor(instance.ConnectionString);
        await context.Database.ExecuteSqlRawAsync("update issue set claim_extended_at = claim_extended_at - interval '1 hour', claim_expires_at = claim_expires_at - interval '1 hour' where number = 1", Ct);
        var before = await Expiry(context, 1);

        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync("/issues/PLAN-1", new { title = "Asked how far you got" }, Ct)).StatusCode);
        Assert.Equal(before, await Expiry(context, 1));

        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct)).StatusCode);
        var afterClaim = await Expiry(context, 1);
        Assert.True(afterClaim > before);

        await context.Database.ExecuteSqlRawAsync("update issue set claim_extended_at = claim_extended_at - interval '1 hour', claim_expires_at = claim_expires_at - interval '1 hour' where number = 1", Ct);
        var beforeEdit = await Expiry(context, 1);
        Assert.Equal(HttpStatusCode.OK, (await agent.PatchAsJsonAsync("/issues/PLAN-1", new { description = "progress" }, Ct)).StatusCode);
        Assert.True(await Expiry(context, 1) > beforeEdit);

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        Assert.Equal(2, await reader.History.CountAsync(h => h.Field == "claim", Ct));
    }

    [Fact]
    public async Task An_expired_claim_reads_as_todo_and_the_successor_writes_the_trace()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Seeded(instance);
        using var one = await AgentAsync(instance, admin, "quiet-otter-42");
        using var two = await AgentAsync(instance, admin, "brisk-heron-7");

        Assert.Equal(HttpStatusCode.OK, (await one.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct)).StatusCode);
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync("update issue set claim_expires_at = now() - interval '1 minute' where number = 1", Ct);
        }

        // Nothing wrote the fallback; the read says todo and nobody.
        var read = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct);
        Assert.Equal("todo", read.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("claim").ValueKind);
        var listed = await admin.GetFromJsonAsync<JsonElement>("/issues?project=PLAN&status=todo&claimed=false", Ct);
        Assert.Contains(listed.GetProperty("items").EnumerateArray(), i => i.GetProperty("key").GetString() == "PLAN-1");

        // The successor takes it, and its history entry says whose it was.
        using var taken = await two.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        Assert.Equal("brisk-heron-7", (await taken.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("claim").GetProperty("holder").GetProperty("name").GetString());

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        var trace = await reader.History.Where(h => h.Field == "claim").OrderByDescending(h => h.Id).FirstAsync(Ct);
        Assert.Equal("expired", trace.Note);
        Assert.NotNull(trace.OldValue);

        // The one whose claim lapsed is told so when it tries to let go.
        var lost = await ProjectEndpointTests.Problem(await one.PostAsync("/issues/PLAN-1/release", null, Ct), HttpStatusCode.Conflict, "claim-lost");
        Assert.Equal("brisk-heron-7", lost.GetProperty("holder").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Force_takes_over_an_agents_claim_and_never_an_agent_over_a_users()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Seeded(instance);
        using var one = await AgentAsync(instance, admin, "quiet-otter-42");
        using var two = await AgentAsync(instance, admin, "brisk-heron-7");

        Assert.Equal(HttpStatusCode.OK, (await one.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct)).StatusCode);
        await ProjectEndpointTests.Problem(await two.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct), HttpStatusCode.Conflict, "claim-held");

        using var forced = await two.PostAsJsonAsync("/issues/PLAN-1/claim", new { force = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        Assert.Equal("brisk-heron-7", (await forced.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("claim").GetProperty("holder").GetProperty("name").GetString());

        // A user's claim: an agent is refused even with force; a user may.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/issues/PLAN-2/claim", new { }, Ct)).StatusCode);
        var protectedClaim = await ProjectEndpointTests.Problem(await one.PostAsJsonAsync("/issues/PLAN-2/claim", new { force = true }, Ct), HttpStatusCode.Forbidden, "claim-protected");
        Assert.Equal("maintainer", protectedClaim.GetProperty("holder").GetProperty("name").GetString());

        using var other = instance.ClientWith(await instance.AddActiveUserAsync("other"));
        var users = await admin.GetFromJsonAsync<JsonElement>("/users", Ct);
        var otherId = users.EnumerateArray().Single(user => user.GetProperty("name").GetString() == "other").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/projects/PLAN/users/{otherId}", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/issues/PLAN-2/claim", new { force = true }, Ct)).StatusCode);

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        Assert.Equal(2, await reader.History.CountAsync(h => h.Field == "claim" && h.Note == "forced", Ct));
    }

    [Fact]
    public async Task A_claim_goes_on_any_open_issue_but_one_in_review_and_release_lands_in_todo()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Seeded(instance);
        using var agent = await AgentAsync(instance, admin, "quiet-otter-42");

        // Parked and blocked: claimable by key, because workability is next's rule.
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("/issues/PLAN-5/claim", new { }, Ct)).StatusCode);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            await context.Database.ExecuteSqlRawAsync("update issue set status = 'review' where number = 3", Ct);
            await context.Database.ExecuteSqlRawAsync("update issue set status = 'done', closed_at = now() where number = 4", Ct);
        }

        await ProjectEndpointTests.Problem(await agent.PostAsJsonAsync("/issues/PLAN-3/claim", new { }, Ct), HttpStatusCode.UnprocessableEntity, "transition");
        await ProjectEndpointTests.Problem(await agent.PostAsJsonAsync("/issues/PLAN-4/claim", new { }, Ct), HttpStatusCode.UnprocessableEntity, "transition");

        // Release: the holder, or a user; lands in todo even when claimed out of backlog.
        using var released = await agent.PostAsync("/issues/PLAN-5/release", null, Ct);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        var issue = await released.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("todo", issue.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("claim").ValueKind);

        await ProjectEndpointTests.Problem(await agent.PostAsync("/issues/PLAN-5/release", null, Ct), HttpStatusCode.UnprocessableEntity, "transition");

        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct)).StatusCode);
        using var byUser = await admin.PostAsync("/issues/PLAN-1/release", null, Ct);
        Assert.Equal(HttpStatusCode.OK, byUser.StatusCode);

        using var another = await AgentAsync(instance, admin, "brisk-heron-7");
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct)).StatusCode);
        await ProjectEndpointTests.Problem(await another.PostAsync("/issues/PLAN-1/release", null, Ct), HttpStatusCode.Conflict, "claim-held");
    }

    private static async Task<HttpClient> Seeded(AnInstance instance)
    {
        var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        using var created = await admin.PostAsJsonAsync("/issues", new
        {
            project = "PLAN",
            issues = new object[]
            {
                new { @ref = "a", title = "A" }, new { title = "B" }, new { title = "C" }, new { title = "D" },
                new { title = "E", status = "backlog", blocked_by = new[] { "a" } },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return admin;
    }

    private static async Task<HttpClient> AgentAsync(AnInstance instance, HttpClient admin, string name)
    {
        using var created = await admin.PostAsJsonAsync("/agents", new { name }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }

    private static async Task<DateTimeOffset?> Expiry(Infrastructure.Persistence.HostingaffeDbContext context, int number)
    {
        context.ChangeTracker.Clear();
        return (await context.Issues.AsNoTracking().SingleAsync(i => i.Number == number, Ct)).Claim?.ExpiresAt;
    }
}
