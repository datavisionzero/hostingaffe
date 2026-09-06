using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Users, agents and tokens over HTTP (<c>docs/api.md</c>): the permission line
/// between a user and an agent, the secret shown once, and revocation that
/// keeps the identity (ADR 0013, ADR 0015).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class IdentityEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_reads_their_email_and_changes_their_name()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        var before = await admin.GetFromJsonAsync<JsonElement>("/api/me", Ct);
        Assert.Equal("maintainer@example.test", before.GetProperty("email").GetString());

        using var changed = await admin.PatchAsJsonAsync("/api/me", new { name = "new maintainer" }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("new maintainer", (await changed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Deactivation_suspends_a_user_and_their_agents_and_reactivation_restores_unrevoked_tokens()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        var userSecret = await instance.AddActiveUserAsync("second");
        using var user = instance.ClientWith(userSecret);
        using var createdAgent = await user.PostAsJsonAsync("/api/agents", new { name = "owned-agent-1" }, Ct);
        var agentSecret = (await createdAgent.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString()!;
        using var agent = instance.ClientWith(agentSecret);
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/users", Ct);
        var id = users.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "second").GetProperty("id").GetGuid();

        using var deactivated = await admin.PostAsync($"/api/users/{id}/deactivate", null, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/me", Ct)).StatusCode);

        using var reactivated = await admin.PostAsync($"/api/users/{id}/reactivate", null, Ct);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/api/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_deactivated_or_demoted()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/users", Ct);
        var id = Assert.Single(users.EnumerateArray()).GetProperty("id").GetGuid();

        await Problem(await admin.PostAsync($"/api/users/{id}/deactivate", null, Ct), HttpStatusCode.Conflict, "last-administrator");
        await Problem(await admin.PatchAsJsonAsync($"/api/users/{id}", new { administrator = false }, Ct),
            HttpStatusCode.Conflict, "last-administrator");
    }

    [Fact]
    public async Task An_agent_reports_partial_metadata_and_every_report_is_kept()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var created = await admin.PostAsJsonAsync("/api/agents", new { name = "quiet-otter-42" }, Ct);
        var agent = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = agent.GetProperty("id").GetGuid();
        using var asAgent = instance.ClientWith(agent.GetProperty("token").GetProperty("secret").GetString()!);

        using var first = await asAgent.PatchAsJsonAsync("/api/me/metadata", new
        {
            kind = "codex", harness = "cli", environment = "container", version = "1.2.3",
        }, Ct);
        Assert.True(first.StatusCode == HttpStatusCode.OK,
            $"{await first.Content.ReadAsStringAsync(Ct)}\n{string.Join('\n', instance.Errors)}");
        var reported = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("codex", reported.GetProperty("metadata").GetProperty("kind").GetString());
        Assert.NotEqual(JsonValueKind.Null, reported.GetProperty("metadata_reported_at").ValueKind);

        // Absent keeps the old value; null clears it.
        using var second = await asAgent.PatchAsJsonAsync("/api/me/metadata", new { harness = (string?)null, version = "1.2.4" }, Ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        reported = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var metadata = reported.GetProperty("metadata");
        Assert.Equal("codex", metadata.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("harness").ValueKind);
        Assert.Equal("container", metadata.GetProperty("environment").GetString());
        Assert.Equal("1.2.4", metadata.GetProperty("version").GetString());

        // A user sees the last report in the management list.
        var agents = await admin.GetFromJsonAsync<JsonElement>("/api/agents", Ct);
        var listed = Assert.Single(agents.EnumerateArray());
        Assert.Equal("1.2.4", listed.GetProperty("metadata").GetProperty("version").GetString());
        Assert.NotEqual(JsonValueKind.Null, listed.GetProperty("metadata_reported_at").ValueKind);

        // The write keeps both complete snapshots; no read endpoint exposes this history yet.
        await using var connection = new NpgsqlConnection(instance.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "select metadata::text from identity_metadata where identity_id = @id order by reported_at", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var snapshots = new List<string>();
        while (await reader.ReadAsync(Ct)) snapshots.Add(reader.GetString(0));
        Assert.Equal(2, snapshots.Count);
        Assert.Contains("\"harness\": \"cli\"", snapshots[0], StringComparison.Ordinal);
        Assert.Contains("\"harness\": null", snapshots[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_an_agent_reports_metadata_and_the_shape_is_closed()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var forbidden = await admin.PatchAsJsonAsync("/api/me/metadata", new { kind = "user" }, Ct);
        await Problem(forbidden, HttpStatusCode.Forbidden, "forbidden", string.Join('\n', instance.Errors));

        using var asAgent = instance.ClientWith(await AgentSecretAsync(admin));
        using var unknown = await asAgent.PatchAsJsonAsync("/api/me/metadata", new { model = "not-stable" }, Ct);
        var unknownProblem = await Problem(unknown, HttpStatusCode.BadRequest, "unknown-field");
        Assert.Equal("model", unknownProblem.GetProperty("field").GetString());

        using var tooLong = await asAgent.PatchAsJsonAsync("/api/me/metadata", new { environment = new string('x', 101) }, Ct);
        var validation = await Problem(tooLong, HttpStatusCode.BadRequest, "validation");
        Assert.True(validation.GetProperty("errors").TryGetProperty("environment", out _));
    }

    [Fact]
    public async Task A_user_creates_an_agent_the_agent_works_and_a_revoked_agent_still_has_its_name()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        // The user creates an agent and gets the secret once.
        using var created = await admin.PostAsJsonAsync("/api/agents", new { name = "quiet-otter-42" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var agent = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("agent", agent.GetProperty("kind").GetString());
        Assert.Equal(AnInstance.Administrator, agent.GetProperty("owner").GetProperty("name").GetString());
        var secret = agent.GetProperty("token").GetProperty("secret").GetString()!;
        Assert.StartsWith("ha_", secret, StringComparison.Ordinal);
        Assert.Equal(secret[..8], agent.GetProperty("token").GetProperty("prefix").GetString());

        // The agent's token authenticates as an agent.
        using var asAgent = instance.ClientWith(secret);
        var me = await asAgent.GetFromJsonAsync<JsonElement>("/api/me", Ct);
        Assert.Equal("agent", me.GetProperty("kind").GetString());
        Assert.Equal("quiet-otter-42", me.GetProperty("name").GetString());

        // The user revokes it; the next call fails.
        var id = agent.GetProperty("id").GetGuid();
        using var revoked = await admin.DeleteAsync($"/api/agents/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using var refused = await asAgent.GetAsync("/api/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        // And the agent still appears by name, with its revocation.
        var agents = await admin.GetFromJsonAsync<JsonElement>("/api/agents", Ct);
        var listed = Assert.Single(agents.EnumerateArray());
        Assert.Equal("quiet-otter-42", listed.GetProperty("name").GetString());
        Assert.NotEqual(JsonValueKind.Null, listed.GetProperty("token").GetProperty("revoked_at").ValueKind);
        Assert.False(listed.GetProperty("token").TryGetProperty("secret", out _));

        // Revoking twice is uneventful.
        using var again = await admin.DeleteAsync($"/api/agents/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task An_agent_without_a_name_is_given_one()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await admin.PostAsJsonAsync("/api/agents", new { }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var agent = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Matches("^[a-z]+-[a-z]+-[0-9]{1,2}$", agent.GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_agent_may_call_none_of_these_and_is_told_why()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var asAgent = instance.ClientWith(await AgentSecretAsync(admin));

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, "/api/users"), (HttpMethod.Get, "/api/users"),
            (HttpMethod.Post, "/api/agents"), (HttpMethod.Get, "/api/agents"),
            (HttpMethod.Get, "/api/tokens"), (HttpMethod.Post, "/api/tokens"),
        })
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post)
            {
                request.Content = JsonContent.Create(new { name = "somebody" });
            }

            using var response = await asAgent.SendAsync(request, Ct);
            await Problem(response, HttpStatusCode.Forbidden, "forbidden", $"{method} {path}");
        }
    }

    [Fact]
    public async Task Only_an_administrator_invites_users()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var asUser = instance.ClientWith(await instance.AddActiveUserAsync("Second Person"));

        // A user who is not an administrator may not invite.
        using var refused = await asUser.PostAsJsonAsync("/api/users", new { name = "third", email = "third@example.test" }, Ct);
        await Problem(refused, HttpStatusCode.Forbidden, "forbidden");

        // Listing all users is instance administration too.
        await Problem(await asUser.GetAsync("/api/users", Ct), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_name_is_unique_across_both_kinds_regardless_of_case()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var taken = await admin.PostAsJsonAsync("/api/agents", new { name = "MAINTAINER" }, Ct);
        var problem = await Problem(taken, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.GetProperty("errors").TryGetProperty("name", out _));

        using var blank = await admin.PostAsJsonAsync("/api/users", new { name = "   " }, Ct);
        await Problem(blank, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task The_owner_renames_and_another_user_may_not()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var other = instance.ClientWith(await instance.AddActiveUserAsync("other"));

        // `other` owns the agent; the administrator does not.
        using var created = await other.PostAsJsonAsync("/api/agents", new { name = "quiet-otter-42" }, Ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        using var renamed = await other.PatchAsJsonAsync($"/api/agents/{id}", new { name = "brisk-heron-7" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("brisk-heron-7", (await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("name").GetString());

        // An administrator may too; a third user may not; an unknown id is not found.
        using var byAdmin = await admin.PatchAsJsonAsync($"/api/agents/{id}", new { name = "calm-badger-3" }, Ct);
        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);

        using var third = instance.ClientWith(await instance.AddActiveUserAsync("third"));
        using var refused = await third.PatchAsJsonAsync($"/api/agents/{id}", new { name = "stolen" }, Ct);
        await Problem(refused, HttpStatusCode.Forbidden, "forbidden");
        using var revokeRefused = await third.DeleteAsync($"/api/agents/{id}", Ct);
        await Problem(revokeRefused, HttpStatusCode.Forbidden, "forbidden");

        using var unknown = await admin.PatchAsJsonAsync($"/api/agents/{Guid.NewGuid()}", new { name = "nobody" }, Ct);
        await Problem(unknown, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task An_identity_is_addressed_by_its_name_as_well_as_by_its_id()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await admin.PostAsJsonAsync("/api/agents", new { name = "quiet-otter-42" }, Ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        // The name addresses the agent, whatever the case, and the rename that
        // answers is the same object the id would have reached.
        using var renamed = await admin.PatchAsJsonAsync("/api/agents/QUIET-OTTER-42", new { name = "brisk-heron-7" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(id, (await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());

        // The old name leads nowhere afterwards; the id still does.
        using var stale = await admin.PatchAsJsonAsync("/api/agents/quiet-otter-42", new { name = "calm-badger-3" }, Ct);
        await Problem(stale, HttpStatusCode.NotFound, "not-found");

        // A name that belongs to a user is not an agent, and misses the same way.
        await instance.AddActiveUserAsync("other");
        using var wrongKind = await admin.PatchAsJsonAsync("/api/agents/other", new { name = "nobody" }, Ct);
        await Problem(wrongKind, HttpStatusCode.NotFound, "not-found");

        using var nobody = await admin.PatchAsJsonAsync("/api/agents/nobody-at-all", new { name = "nobody" }, Ct);
        await Problem(nobody, HttpStatusCode.NotFound, "not-found");

        // The user side of the same rule, and the revoke that ends it.
        using var deactivated = await admin.PostAsync("/api/users/other/deactivate", null, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal("deactivated", (await deactivated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("state").GetString());

        using var missing = await admin.PostAsync("/api/users/nobody-at-all/deactivate", null, Ct);
        await Problem(missing, HttpStatusCode.NotFound, "not-found");

        using var revoked = await admin.DeleteAsync("/api/agents/brisk-heron-7", Ct);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
    }

    [Fact]
    public async Task A_user_has_as_many_tokens_as_they_create_and_revokes_only_their_own()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await admin.PostAsync("/api/tokens", null, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var issued = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var secret = issued.GetProperty("secret").GetString()!;
        var id = issued.GetProperty("id").GetGuid();

        using var withNew = instance.ClientWith(secret);
        Assert.Equal(HttpStatusCode.OK, (await withNew.GetAsync("/api/me", Ct)).StatusCode);

        var tokens = await admin.GetFromJsonAsync<JsonElement>("/api/tokens", Ct);
        Assert.Equal(2, tokens.GetArrayLength());
        Assert.All(tokens.EnumerateArray(), t => Assert.False(t.TryGetProperty("secret", out _)));

        // Another user cannot see, and so cannot revoke, this token.
        using var other = instance.ClientWith(await instance.AddActiveUserAsync("other"));
        using var notTheirs = await other.DeleteAsync($"/api/tokens/{id}", Ct);
        await Problem(notTheirs, HttpStatusCode.NotFound, "not-found");

        // The owner revokes it, and it stops working.
        using var revoked = await admin.DeleteAsync($"/api/tokens/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withNew.GetAsync("/api/me", Ct)).StatusCode);

        tokens = await admin.GetFromJsonAsync<JsonElement>("/api/tokens", Ct);
        Assert.Single(tokens.EnumerateArray(), t => t.GetProperty("revoked_at").ValueKind != JsonValueKind.Null);
    }

    private static async Task<string> AgentSecretAsync(HttpClient user)
    {
        using var created = await user.PostAsJsonAsync("/api/agents", new { }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString()!;
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage response, HttpStatusCode status, string code, string? on = null)
    {
        Assert.True(status == response.StatusCode, $"{on}: expected {status}, got {response.StatusCode}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal($"/problems/{code}", problem.GetProperty("type").GetString());
        return problem;
    }
}
