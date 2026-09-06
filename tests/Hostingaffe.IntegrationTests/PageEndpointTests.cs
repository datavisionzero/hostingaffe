using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Pages over HTTP (<c>docs/api.md</c>, Pages): the project's flat wiki,
/// addressed by a slug (ADR 0021), open to agents like every other piece of
/// project content (ADR 0015).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PageEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_writes_a_page_and_the_list_is_slim()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var agent = await Agent(instance, admin, "one");

        using var created = await agent.PostAsJsonAsync(
            "/projects/PLAN/pages",
            new { slug = "architecture", title = "Architecture", body = "# The four layers" },
            Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/projects/PLAN/pages/architecture", created.Headers.Location?.ToString());
        var page = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("architecture", page.GetProperty("slug").GetString());
        Assert.Equal("PLAN", page.GetProperty("project").GetString());
        Assert.Equal("# The four layers", page.GetProperty("body").GetString());
        Assert.Equal("one", page.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("one", page.GetProperty("updated_by").GetProperty("name").GetString());

        await agent.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "onboarding", title = "Onboarding" }, Ct);

        // The list is by slug and carries no body: a wiki of thirty pages would
        // otherwise be a context eater (ADR 0012).
        var list = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages", Ct);
        Assert.Equal(["architecture", "onboarding"], list.EnumerateArray().Select(p => p.GetProperty("slug").GetString()));
        Assert.False(list[0].TryGetProperty("body", out _));

        // The empty page is a page: an absent body is the empty document.
        Assert.Equal(string.Empty, (await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages/onboarding", Ct)).GetProperty("body").GetString());
    }

    [Fact]
    public async Task A_slug_is_given_and_validated_and_taken_only_once()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);

        await ProjectEndpointTests.Problem(
            await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "Not A Slug", title = "No" }, Ct),
            HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(
            await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Architecture" }, Ct);
        await ProjectEndpointTests.Problem(
            await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Again" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        // An address that could not be a slug names nothing; it arrived in the
        // path and not in a body, so it is `not-found` and not `validation`.
        await ProjectEndpointTests.Problem(await admin.GetAsync("/projects/PLAN/pages/Nothing%20Here", Ct), HttpStatusCode.NotFound, "not-found");
        await ProjectEndpointTests.Problem(await admin.GetAsync("/projects/PLAN/pages/onboarding", Ct), HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task The_document_is_guarded_and_every_change_is_history()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        using var created = await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Architecture", body = "v1" }, Ct);
        var version = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("updated_at").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/projects/PLAN/pages/architecture")
        {
            Content = JsonContent.Create(new { title = "The four layers", body = "v2" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var changed = await admin.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("v2", after.GetProperty("body").GetString());
        Assert.Equal("The four layers", after.GetProperty("title").GetString());

        using var staleRequest = new HttpRequestMessage(HttpMethod.Patch, "/projects/PLAN/pages/architecture")
        {
            Content = JsonContent.Create(new { body = "v3" }),
        };
        staleRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var problem = await ProjectEndpointTests.Problem(await admin.SendAsync(staleRequest, Ct), HttpStatusCode.PreconditionFailed, "stale");

        // The refusal carries the current page, so the client can merge rather
        // than lose what it typed.
        Assert.Equal("v2", problem.GetProperty("current").GetProperty("body").GetString());

        // Without the header the write goes through, as everywhere.
        using var unguarded = await admin.PatchAsJsonAsync("/projects/PLAN/pages/architecture", new { body = "v3" }, Ct);
        Assert.Equal(HttpStatusCode.OK, unguarded.StatusCode);

        // An explicit null empties the document; an absent body leaves it.
        using var emptied = await admin.PatchAsJsonAsync("/projects/PLAN/pages/architecture", new { body = (string?)null }, Ct);
        Assert.Equal(string.Empty, (await emptied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("body").GetString());
        using var untouched = await admin.PatchAsJsonAsync("/projects/PLAN/pages/architecture", new { title = "Architecture" }, Ct);
        Assert.Equal(string.Empty, (await untouched.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("body").GetString());

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        var fields = await reader.History.OrderBy(h => h.Id).Select(h => h.Field).ToListAsync(Ct);
        Assert.Equal(["created", "title", "body", "body", "body", "title"], fields);
        Assert.All(await reader.History.Where(h => h.Field == "body").ToListAsync(Ct), entry => Assert.Null(entry.NewValue));
    }

    [Fact]
    public async Task Renaming_moves_the_address_and_leaves_nothing_behind()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Architecture", body = "# The four layers" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "onboarding", title = "Onboarding" }, Ct);

        await ProjectEndpointTests.Problem(
            await admin.PatchAsJsonAsync("/projects/PLAN/pages/architecture", new { slug = "onboarding" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        using var renamed = await admin.PatchAsJsonAsync("/projects/PLAN/pages/architecture", new { slug = "betriebshandbuch" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var page = await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("betriebshandbuch", page.GetProperty("slug").GetString());
        Assert.Equal("# The four layers", page.GetProperty("body").GetString());

        // Nothing forwards: the old address is gone (ADR 0021).
        await ProjectEndpointTests.Problem(await admin.GetAsync("/projects/PLAN/pages/architecture", Ct), HttpStatusCode.NotFound, "not-found");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/projects/PLAN/pages/betriebshandbuch", Ct)).StatusCode);

        // The rename is the one place the old name survives.
        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        var entry = await reader.History.SingleAsync(h => h.Field == "slug", Ct);
        Assert.Equal("architecture", entry.OldValue);
        Assert.Equal("betriebshandbuch", entry.NewValue);
    }

    [Fact]
    public async Task Deleting_keeps_the_slug_and_restoring_gives_the_page_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Architecture", body = "# The four layers" }, Ct);

        using var deleted = await admin.DeleteAsync("/projects/PLAN/pages/architecture", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await ProjectEndpointTests.Problem(await admin.GetAsync("/projects/PLAN/pages/architecture", Ct), HttpStatusCode.NotFound, "deleted");
        Assert.True(gone.TryGetProperty("restorable_until", out _));
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/pages", Ct)).EnumerateArray());

        // The slug is not free while the page can come back.
        await ProjectEndpointTests.Problem(
            await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Something else" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        using var restored = await admin.PostAsync("/projects/PLAN/pages/architecture/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal("# The four layers", (await restored.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("body").GetString());
        await ProjectEndpointTests.Problem(
            await admin.PostAsync("/projects/PLAN/pages/architecture/restore", null, Ct),
            HttpStatusCode.UnprocessableEntity, "transition");
    }

    /// <summary>
    /// The search is what the flat wiki has instead of a hierarchy (VISION 7),
    /// so it has to find what the navigation would have led to.
    /// </summary>
    [Fact]
    public async Task The_search_finds_a_page_by_its_title_and_by_its_body()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Architecture", body = "Dependencies point inward and only inward." }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "onboarding", title = "Onboarding", body = "Start with docker compose up." }, Ct);

        Assert.Equal(["architecture"], await Found(admin, "inward"));
        Assert.Equal(["architecture"], await Found(admin, "Architecture"));
        Assert.Equal(["onboarding"], await Found(admin, "\"docker compose\""));

        // The `simple` configuration, so an identifier survives being searched for.
        Assert.Equal(["onboarding"], await Found(admin, "-inward compose"));
        Assert.Empty(await Found(admin, "nothing here"));

        // A filter, not a ranking: the order stays the slug's.
        Assert.Equal(["architecture", "onboarding"], await Found(admin, "docker OR inward"));

        // A deleted page is not found while it is in its grace period (ADR 0013).
        await admin.DeleteAsync("/projects/PLAN/pages/architecture", Ct);
        Assert.Empty(await Found(admin, "inward"));
    }

    /// <summary>
    /// The wiki is project content, so the project scope is what decides who
    /// sees it — one rule, no second permission model (VISION 7).
    /// </summary>
    [Fact]
    public async Task A_project_the_caller_cannot_reach_has_no_pages()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "architecture", title = "Architecture" }, Ct);

        var outsider = await instance.AddActiveUserAsync("outsider");
        using var stranger = instance.ClientWith(outsider);

        await ProjectEndpointTests.Problem(await stranger.GetAsync("/projects/PLAN/pages", Ct), HttpStatusCode.NotFound, "not-found");
        await ProjectEndpointTests.Problem(await stranger.GetAsync("/projects/PLAN/pages/architecture", Ct), HttpStatusCode.NotFound, "not-found");
        await ProjectEndpointTests.Problem(
            await stranger.PostAsJsonAsync("/projects/PLAN/pages", new { slug = "sneaky", title = "Sneaky" }, Ct),
            HttpStatusCode.NotFound, "not-found");
    }

    private static async Task<string[]> Found(HttpClient client, string query) =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"/projects/PLAN/pages?q={Uri.EscapeDataString(query)}", Ct))
            .EnumerateArray().Select(p => p.GetProperty("slug").GetString()!)];

    private static async Task<HttpClient> Project(AnInstance instance)
    {
        var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var project = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        return admin;
    }

    private static async Task<HttpClient> Agent(AnInstance instance, HttpClient admin, string name)
    {
        using var created = await admin.PostAsJsonAsync("/agents", new { name }, Ct);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }
}
