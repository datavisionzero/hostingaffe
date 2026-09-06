using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>Projects over HTTP (<c>docs/api.md</c>, Projects).</summary>
[Collection(nameof(PostgresCollection))]
public sealed class ProjectEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administration_lists_live_and_deleted_projects()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "LIVE", name = "live" }, Ct);
        await admin.PostAsJsonAsync("/projects", new { key = "GONE", name = "gone" }, Ct);
        await admin.DeleteAsync("/projects/GONE", Ct);

        var all = await admin.GetFromJsonAsync<JsonElement>("/admin/projects?deleted=all", Ct);
        Assert.Equal(2, all.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, all.EnumerateArray().Single(x => x.GetProperty("key").GetString() == "LIVE").GetProperty("deleted_at").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, all.EnumerateArray().Single(x => x.GetProperty("key").GetString() == "GONE").GetProperty("deleted_at").ValueKind);
    }

    [Fact]
    public async Task A_project_round_trips()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/projects/PLAN", created.Headers.Location?.ToString());
        var project = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("PLAN", project.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("instructions_page").ValueKind);

        var read = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN", Ct);
        Assert.Equal("hostingaffe", read.GetProperty("name").GetString());

        var listed = await admin.GetFromJsonAsync<JsonElement>("/projects", Ct);
        Assert.Equal("PLAN", Assert.Single(listed.EnumerateArray()).GetProperty("key").GetString());

        // PATCH changes what is present and nothing else.
        using var changed = await admin.PatchAsJsonAsync("/projects/PLAN", new { name = "renamed" }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("renamed", after.GetProperty("name").GetString());
        Assert.Equal("PLAN", after.GetProperty("key").GetString());
    }

    [Fact]
    public async Task A_key_is_the_pattern_and_is_taken_even_by_a_deleted_project()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var lower = await admin.PostAsJsonAsync("/projects", new { key = "plan", name = "x" }, Ct);
        var problem = await Problem(lower, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.GetProperty("errors").TryGetProperty("key", out _));

        using var first = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "x" }, Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var deleted = await admin.DeleteAsync("/projects/PLAN", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var again = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "y" }, Ct);
        await Problem(again, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Deleting_hides_a_project_and_restoring_brings_it_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);

        using var deleted = await admin.DeleteAsync("/projects/PLAN", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var listed = await admin.GetFromJsonAsync<JsonElement>("/projects", Ct);
        Assert.Empty(listed.EnumerateArray());

        using var read = await admin.GetAsync("/projects/PLAN", Ct);
        var problem = await Problem(read, HttpStatusCode.NotFound, "deleted");
        Assert.True(problem.TryGetProperty("restorable_until", out _));

        using var pages = await admin.GetAsync("/projects/PLAN/pages", Ct);
        await Problem(pages, HttpStatusCode.NotFound, "deleted");

        using var restored = await admin.PostAsync("/projects/PLAN/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/projects/PLAN", Ct)).StatusCode);

        using var notDeleted = await admin.PostAsync("/projects/PLAN/restore", null, Ct);
        await Problem(notDeleted, HttpStatusCode.UnprocessableEntity, "transition");

        using var unknown = await admin.GetAsync("/projects/NOPE", Ct);
        await Problem(unknown, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Project_access_is_granted_to_users_inherited_by_agents_and_hidden_as_not_found()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);

        var otherToken = await instance.AddActiveUserAsync("other");
        using var user = instance.ClientWith(otherToken);
        using var users = await admin.GetAsync("/users", Ct);
        var otherId = (await users.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray()
            .Single(value => value.GetProperty("name").GetString() == "other").GetProperty("id").GetGuid();

        using var otherAgentResponse = await user.PostAsJsonAsync("/agents", new { }, Ct);
        using var agent = instance.ClientWith((await otherAgentResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());

        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/projects", Ct)).StatusCode);
        Assert.Empty((await agent.GetFromJsonAsync<JsonElement>("/projects", Ct)).EnumerateArray());
        await Problem(await agent.GetAsync("/projects/PLAN", Ct), HttpStatusCode.NotFound, "not-found");
        await Problem(await user.GetAsync("/projects/PLAN/pages", Ct), HttpStatusCode.NotFound, "not-found");

        using var granted = await admin.PutAsync($"/projects/PLAN/users/{otherId}", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, granted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/projects/PLAN", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PatchAsJsonAsync("/projects/PLAN", new { name = "renamed" }, Ct)).StatusCode);

        var assigned = await user.GetFromJsonAsync<JsonElement>("/projects/PLAN/users", Ct);
        Assert.Equal(["maintainer", "other"], assigned.EnumerateArray().Select(value => value.GetProperty("name").GetString()).Order());

        using var revoked = await admin.DeleteAsync($"/projects/PLAN/users/{otherId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        await Problem(await agent.GetAsync("/projects/PLAN", Ct), HttpStatusCode.NotFound, "not-found");

        await Problem(await agent.PostAsJsonAsync("/projects", new { key = "AG", name = "x" }, Ct), HttpStatusCode.Forbidden, "forbidden");
        await Problem(await agent.PatchAsJsonAsync("/projects/PLAN", new { name = "x" }, Ct), HttpStatusCode.NotFound, "not-found");
        await Problem(await user.DeleteAsync("/projects/PLAN", Ct), HttpStatusCode.Forbidden, "forbidden");
    }

    /// <summary>
    /// Routing matches literal segments without regard to case, so every
    /// project-content route answers under a spelling the scope guard has to
    /// recognise as its own. It once compared the request path and let
    /// <c>/Projects/PLAN</c> past with no check at all.
    /// </summary>
    [Fact]
    public async Task Project_scope_holds_when_the_route_is_spelled_in_another_case()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "secret", title = "Secret work" }, Ct);

        using var outsider = instance.ClientWith(await instance.AddActiveUserAsync("outsider"));
        foreach (var (method, path, body) in new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/Projects/PLAN", null),
            (HttpMethod.Patch, "/Projects/PLAN", new { name = "hijacked" }),
            (HttpMethod.Get, "/Projects/PLAN/pages", null),
            (HttpMethod.Post, "/Projects/PLAN/pages", new { slug = "planted", title = "Planted" }),
            (HttpMethod.Get, "/Projects/PLAN/pages/secret", null),
            (HttpMethod.Patch, "/Projects/PLAN/pages/secret", new { title = "hijacked" }),
            (HttpMethod.Delete, "/Projects/PLAN/pages/secret", null),
        })
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            await Problem(await outsider.SendAsync(request, Ct), HttpStatusCode.NotFound, "not-found");
        }

        // Nothing the outsider sent was written: the refusal came before the handler.
        Assert.Equal("hostingaffe", (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN", Ct)).GetProperty("name").GetString());
        Assert.Equal(
            ["secret"],
            (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages", Ct))
                .EnumerateArray().Select(page => page.GetProperty("slug").GetString()));
        Assert.Equal("Secret work", (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages/secret", Ct)).GetProperty("title").GetString());
    }

    /// <summary>
    /// The page every agent is handed with its work (VISION 15.3): a user
    /// designates it and an agent may not.
    /// </summary>
    [Fact]
    public async Task The_instructions_page_is_designated_by_a_user_and_follows_the_page_it_names()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await admin.PostAsJsonAsync(
            "/projects/PLAN/pages", new { slug = "agents", title = "How work runs here", body = "Tests run with `just test`." }, Ct);

        // Designated by nobody yet.
        var project = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN", Ct);
        Assert.Equal(JsonValueKind.Null, project.GetProperty("instructions_page").ValueKind);

        // A slug that names nothing is refused at the field it arrived in.
        using var unknown = await admin.PatchAsJsonAsync("/projects/PLAN", new { instructions_page = "nowhere" }, Ct);
        var problem = await Problem(unknown, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.GetProperty("errors").TryGetProperty("instructions_page", out _));

        using var designated = await admin.PatchAsJsonAsync("/projects/PLAN", new { instructions_page = "agents" }, Ct);
        Assert.Equal(HttpStatusCode.OK, designated.StatusCode);
        Assert.Equal("agents", (await designated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("instructions_page").GetString());

        using var agent = await Agent(instance, admin, "worker");

        // An agent may not point the project at a page: it would be writing its own instructions.
        using var byAgent = await agent.PatchAsJsonAsync("/projects/PLAN", new { instructions_page = "agents" }, Ct);
        await Problem(byAgent, HttpStatusCode.Forbidden, "forbidden");

        // Renaming the page leaves the designation where it was: the pointer is the row, not the address.
        using var renamed = await admin.PatchAsJsonAsync("/projects/PLAN/pages/agents", new { slug = "house-rules" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(
            "house-rules",
            (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN", Ct)).GetProperty("instructions_page").GetString());

        // Deleting it goes quiet rather than refusing, and the restore brings it back.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/projects/PLAN/pages/house-rules", Ct)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN", Ct)).GetProperty("instructions_page").ValueKind);

        using var restored = await admin.PostAsync("/projects/PLAN/pages/house-rules/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(
            "house-rules",
            (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN", Ct)).GetProperty("instructions_page").GetString());

        // And `null` takes the designation away, where leaving the field out leaves it alone.
        using var kept = await admin.PatchAsJsonAsync("/projects/PLAN", new { name = "hostingaffe" }, Ct);
        Assert.Equal("house-rules", (await kept.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("instructions_page").GetString());

        using var cleared = await admin.PatchAsJsonAsync("/projects/PLAN", new { instructions_page = (string?)null }, Ct);
        Assert.Equal(JsonValueKind.Null, (await cleared.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("instructions_page").ValueKind);
    }

    private static async Task<HttpClient> Agent(AnInstance instance, HttpClient admin, string name)
    {
        using var created = await admin.PostAsJsonAsync("/agents", new { name }, Ct);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }

    internal static async Task<JsonElement> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal($"/problems/{code}", problem.GetProperty("type").GetString());
            return problem;
        }
    }
}
