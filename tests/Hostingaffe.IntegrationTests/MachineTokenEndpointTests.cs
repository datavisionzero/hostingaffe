using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The key a machine reports under (<c>docs/api.md</c>, Machines; ADR 0016):
/// issued and revoked by a person, read by anyone, and never shown twice.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MachineTokenEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_token_is_issued_once_and_the_secret_is_never_read_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var issued = await admin.PostAsJsonAsync("/api/machines/ex44/token", new { }, Ct);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);

        var token = await issued.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var secret = token.GetProperty("secret").GetString()!;
        Assert.StartsWith("ha_", secret);
        Assert.Equal(secret[..8], token.GetProperty("prefix").GetString());

        var read = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/token", Ct);
        Assert.True(read.GetProperty("present").GetBoolean());
        Assert.Equal(secret[..8], read.GetProperty("prefix").GetString());
        Assert.Equal("maintainer", read.GetProperty("issued_by").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("last_used_at").ValueKind);
        Assert.DoesNotContain(read.EnumerateObject(), field => field.Name is "secret");

        // The token works, and using it is what moves `last_used_at`.
        using var machine = instance.ClientWith(secret);
        using var handed = await machine.PostAsJsonAsync("/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);

        var used = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/token", Ct);
        Assert.Equal(JsonValueKind.String, used.GetProperty("last_used_at").ValueKind);
    }

    [Fact]
    public async Task A_second_issue_is_refused_and_rotating_replaces_the_first()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var first = await admin.PostAsJsonAsync("/api/machines/ex44/token", new { }, Ct);
        var old = (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("secret").GetString()!;

        await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/machines/ex44/token", new { }, Ct),
            HttpStatusCode.UnprocessableEntity,
            "transition");

        using var rotated = await admin.PostAsJsonAsync("/api/machines/ex44/token?rotate=true", new { }, Ct);
        Assert.Equal(HttpStatusCode.Created, rotated.StatusCode);
        var fresh = (await rotated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("secret").GetString()!;
        Assert.NotEqual(old, fresh);

        // The host fails visibly at its next run rather than carrying on.
        using var stale = instance.ClientWith(old);
        await Refusals.Problem(
            await stale.PostAsJsonAsync("/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct),
            HttpStatusCode.Unauthorized,
            "unauthenticated");

        using var machine = instance.ClientWith(fresh);
        using var handed = await machine.PostAsJsonAsync("/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);
    }

    [Fact]
    public async Task Revoking_stops_the_reporting_and_leaves_the_row_standing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var issued = await admin.PostAsJsonAsync("/api/machines/ex44/token", new { }, Ct);
        var secret = (await issued.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("secret").GetString()!;

        using var revoked = await admin.DeleteAsync("/api/machines/ex44/token", Ct);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using var machine = instance.ClientWith(secret);
        await Refusals.Problem(
            await machine.PostAsJsonAsync("/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct),
            HttpStatusCode.Unauthorized,
            "unauthenticated");

        // The row stays, so that "there was one, and who took it back when" has
        // an answer.
        var read = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/token", Ct);
        Assert.False(read.GetProperty("present").GetBoolean());
        Assert.Equal(secret[..8], read.GetProperty("prefix").GetString());
        Assert.Equal("maintainer", read.GetProperty("revoked_by").GetProperty("name").GetString());

        await Refusals.Problem(
            await admin.DeleteAsync("/api/machines/ex44/token", Ct), HttpStatusCode.NotFound, "not-found");
    }

    /// <summary>
    /// Issuing and revoking write a history row on the machine — the one thing
    /// around reports that belongs in the history, because a person changed what
    /// the machine may do (ADR 0015).
    /// </summary>
    [Fact]
    public async Task Issuing_and_revoking_are_in_the_machines_history()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var issued = await admin.PostAsJsonAsync("/api/machines/ex44/token?note=for%20the%20cron", new { }, Ct);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        using var revoked = await admin.DeleteAsync("/api/machines/ex44/token", Ct);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/history", Ct);
        Assert.Equal(
            ["created", "token", "token"],
            history.EnumerateArray().Select(entry => entry.GetProperty("field").GetString()));
        Assert.Equal(
            "for the cron",
            history.EnumerateArray().ElementAt(1).GetProperty("note").GetString());
    }

    [Fact]
    public async Task An_agent_may_look_and_may_not_issue_or_revoke()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var created = await admin.PostAsJsonAsync("/api/agents", new { name = "one" }, Ct);
        using var agent = instance.ClientWith(
            (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());

        await Refusals.Problem(
            await agent.PostAsJsonAsync("/api/machines/ex44/token", new { }, Ct),
            HttpStatusCode.Forbidden,
            "forbidden");

        using var issued = await admin.PostAsJsonAsync("/api/machines/ex44/token", new { }, Ct);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);

        // There is no secret in what it reads, so it may read it.
        var read = await agent.GetFromJsonAsync<JsonElement>("/api/machines/ex44/token", Ct);
        Assert.True(read.GetProperty("present").GetBoolean());

        await Refusals.Problem(
            await agent.DeleteAsync("/api/machines/ex44/token", Ct), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_machine_token_administers_nothing_not_even_itself()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        await Refusals.Problem(
            await machine.GetAsync("/api/machines/ex44/token", Ct), HttpStatusCode.Unauthorized, "unauthenticated");
        await Refusals.Problem(
            await machine.PostAsJsonAsync("/api/machines/ex44/token", new { }, Ct),
            HttpStatusCode.Unauthorized,
            "unauthenticated");
        await Refusals.Problem(
            await machine.DeleteAsync("/api/machines/ex44/token", Ct), HttpStatusCode.Unauthorized, "unauthenticated");
    }
}
