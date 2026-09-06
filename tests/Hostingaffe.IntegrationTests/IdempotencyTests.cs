using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// <c>Idempotency-Key</c> on every write (<c>docs/api.md</c>): a replay is
/// answered from the store and creates nothing, a reuse for another request is
/// refused, and keys of different identities never meet.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class IdempotencyTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_create_replayed_with_the_same_key_returns_the_same_page_and_creates_nothing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        var body = new { slug = "architecture", title = "Architecture", body = "# The four layers" };

        using var first = await Send(admin, HttpMethod.Post, "/projects/PLAN/pages", body, "create-architecture");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync(Ct);

        using var replay = await Send(admin, HttpMethod.Post, "/projects/PLAN/pages", body, "create-architecture");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        // The same answer — structurally: the store is jsonb, which spells its JSON its own way.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(firstBody), JsonNode.Parse(await replay.Content.ReadAsStringAsync(Ct))));
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotent-Replayed")));

        Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages", Ct)).EnumerateArray());

        // The same key with a different body is a reuse, not a replay.
        var mismatch = await ProjectEndpointTests.Problem(
            await Send(admin, HttpMethod.Post, "/projects/PLAN/pages", new { slug = "other", title = "Other" }, "create-architecture"),
            HttpStatusCode.Conflict, "idempotency-mismatch");
        Assert.Contains("create-architecture", mismatch.GetProperty("detail").GetString(), StringComparison.Ordinal);

        // A fresh key creates again.
        using var fresh = await Send(
            admin, HttpMethod.Post, "/projects/PLAN/pages", new { slug = "onboarding", title = "Onboarding" }, "create-onboarding");
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages", Ct)).EnumerateArray().Count());
    }

    [Fact]
    public async Task Keys_of_different_identities_never_meet_and_a_refusal_is_replayed_too()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var other = instance.ClientWith(await instance.AddActiveUserAsync("other"));
        var users = await admin.GetFromJsonAsync<JsonElement>("/users", Ct);
        var otherId = users.EnumerateArray().Single(user => user.GetProperty("name").GetString() == "other").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/projects/PLAN/users/{otherId}", null, Ct)).StatusCode);

        using var mine = await Send(admin, HttpMethod.Post, "/projects/PLAN/pages", new { slug = "mine", title = "Mine" }, "shared-key");
        using var theirs = await Send(other, HttpMethod.Post, "/projects/PLAN/pages", new { slug = "theirs", title = "Theirs" }, "shared-key");
        Assert.Equal(HttpStatusCode.Created, mine.StatusCode);
        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages", Ct)).EnumerateArray().Count());

        // A refused write is kept and replayed as the same refusal.
        var bad = new { slug = "", title = "" };
        await ProjectEndpointTests.Problem(
            await Send(admin, HttpMethod.Post, "/projects/PLAN/pages", bad, "bad-key"), HttpStatusCode.BadRequest, "validation");
        using var replayed = await Send(admin, HttpMethod.Post, "/projects/PLAN/pages", bad, "bad-key");
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        Assert.Equal("application/problem+json", replayed.Content.Headers.ContentType?.MediaType);
        Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotent-Replayed")));

        // A key on a read is ignored; an overlong key is refused; a key without a caller changes nothing about the 401.
        using var read = await Send(admin, HttpMethod.Get, "/projects/PLAN/pages", null, "shared-key");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await ProjectEndpointTests.Problem(
            await Send(admin, HttpMethod.Post, "/projects/PLAN/pages", bad, new string('k', 201)), HttpStatusCode.BadRequest, "validation");
        using var anonymous = instance.ClientWith(null);
        using var refused = await Send(anonymous, HttpMethod.Post, "/projects/PLAN/pages", bad, "no-caller");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, object? body, string key)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request, Ct);
    }

    private static async Task<HttpClient> Project(AnInstance instance)
    {
        var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var project = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        return admin;
    }
}
