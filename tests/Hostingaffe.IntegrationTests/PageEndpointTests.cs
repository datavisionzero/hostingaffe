using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Pages over HTTP (<c>docs/api.md</c>, Pages): the instance's flat wiki,
/// addressed by a slug (ADR 0021), open to agents like every other piece of
/// content (planaffe ADR 0015).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PageEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_writes_a_page_and_the_list_is_slim()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var agent = await Agent(instance, admin, "one");

        using var created = await agent.PostAsJsonAsync(
            "/api/pages",
            new { slug = "architecture", title = "Architecture", body = "# The four layers" },
            Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/pages/architecture", created.Headers.Location?.ToString());
        var page = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("architecture", page.GetProperty("slug").GetString());
        Assert.Equal("# The four layers", page.GetProperty("body").GetString());
        Assert.Equal("one", page.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("one", page.GetProperty("updated_by").GetProperty("name").GetString());

        await agent.PostAsJsonAsync("/api/pages", new { slug = "onboarding", title = "Onboarding" }, Ct);

        // The list is by slug and carries no body: a wiki of thirty pages would
        // otherwise be a context eater (ADR 0012).
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/pages", Ct);
        Assert.Equal(["architecture", "onboarding"], list.EnumerateArray().Select(p => p.GetProperty("slug").GetString()));
        Assert.False(list[0].TryGetProperty("body", out _));

        // The empty page is a page: an absent body is the empty document.
        Assert.Equal(string.Empty, (await admin.GetFromJsonAsync<JsonElement>("/api/pages/onboarding", Ct)).GetProperty("body").GetString());
    }

    [Fact]
    public async Task A_slug_is_given_and_validated_and_taken_only_once()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/pages", new { slug = "Not A Slug", title = "No" }, Ct),
            HttpStatusCode.BadRequest, "validation");
        await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Architecture" }, Ct);
        await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Again" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        // An address that could not be a slug names nothing; it arrived in the
        // path and not in a body, so it is `not-found` and not `validation`.
        await Refusals.Problem(await admin.GetAsync("/api/pages/Nothing%20Here", Ct), HttpStatusCode.NotFound, "not-found");
        await Refusals.Problem(await admin.GetAsync("/api/pages/onboarding", Ct), HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task The_document_is_guarded_and_every_change_is_history()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var created = await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Architecture", body = "v1" }, Ct);
        var version = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("updated_at").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/pages/architecture")
        {
            Content = JsonContent.Create(new { title = "The four layers", body = "v2" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var changed = await admin.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("v2", after.GetProperty("body").GetString());
        Assert.Equal("The four layers", after.GetProperty("title").GetString());

        using var staleRequest = new HttpRequestMessage(HttpMethod.Patch, "/api/pages/architecture")
        {
            Content = JsonContent.Create(new { body = "v3" }),
        };
        staleRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var problem = await Refusals.Problem(await admin.SendAsync(staleRequest, Ct), HttpStatusCode.PreconditionFailed, "stale");

        // The refusal carries the current page, so the client can merge rather
        // than lose what it typed.
        Assert.Equal("v2", problem.GetProperty("current").GetProperty("body").GetString());

        // Without the header the write goes through, as everywhere.
        using var unguarded = await admin.PatchAsJsonAsync("/api/pages/architecture", new { body = "v3" }, Ct);
        Assert.Equal(HttpStatusCode.OK, unguarded.StatusCode);

        // An explicit null empties the document; an absent body leaves it.
        using var emptied = await admin.PatchAsJsonAsync("/api/pages/architecture", new { body = (string?)null }, Ct);
        Assert.Equal(string.Empty, (await emptied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("body").GetString());
        using var untouched = await admin.PatchAsJsonAsync("/api/pages/architecture", new { title = "Architecture" }, Ct);
        Assert.Equal(string.Empty, (await untouched.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("body").GetString());

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        var fields = await reader.History.OrderBy(h => h.Id).Select(h => h.Field).ToListAsync(Ct);
        Assert.Equal(["created", "title", "body", "body", "body", "title"], fields);
        Assert.All(await reader.History.Where(h => h.Field == "body").ToListAsync(Ct), entry => Assert.Null(entry.NewValue));

        // The same history, as the API serves it: who, when, from what to what.
        var served = await admin.GetFromJsonAsync<JsonElement>("/api/pages/architecture/history", Ct);
        Assert.Equal(fields, served.EnumerateArray().Select(entry => entry.GetProperty("field").GetString()));
        var titled = served.EnumerateArray().Last(entry => entry.GetProperty("field").GetString() == "title");
        Assert.Equal("The four layers", titled.GetProperty("old_value").GetString());
        Assert.Equal("Architecture", titled.GetProperty("new_value").GetString());
        Assert.Equal("maintainer", titled.GetProperty("actor").GetProperty("name").GetString());
        // A text records that it changed, not how.
        var body = served.EnumerateArray().First(entry => entry.GetProperty("field").GetString() == "body");
        Assert.Equal(JsonValueKind.Null, body.GetProperty("new_value").ValueKind);
    }

    [Fact]
    public async Task Renaming_moves_the_address_and_leaves_nothing_behind()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Architecture", body = "# The four layers" }, Ct);
        await admin.PostAsJsonAsync("/api/pages", new { slug = "onboarding", title = "Onboarding" }, Ct);

        await Refusals.Problem(
            await admin.PatchAsJsonAsync("/api/pages/architecture", new { slug = "onboarding" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        using var renamed = await admin.PatchAsJsonAsync("/api/pages/architecture", new { slug = "betriebshandbuch" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var page = await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("betriebshandbuch", page.GetProperty("slug").GetString());
        Assert.Equal("# The four layers", page.GetProperty("body").GetString());

        // Nothing forwards: the old address is gone (ADR 0021).
        await Refusals.Problem(await admin.GetAsync("/api/pages/architecture", Ct), HttpStatusCode.NotFound, "not-found");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/pages/betriebshandbuch", Ct)).StatusCode);

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
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Architecture", body = "# The four layers" }, Ct);

        using var deleted = await admin.DeleteAsync("/api/pages/architecture", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await Refusals.Problem(await admin.GetAsync("/api/pages/architecture", Ct), HttpStatusCode.NotFound, "deleted");
        Assert.True(gone.TryGetProperty("restorable_until", out _));
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/api/pages", Ct)).EnumerateArray());

        // The slug is not free while the page can come back.
        await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Something else" }, Ct),
            HttpStatusCode.BadRequest, "validation");

        using var restored = await admin.PostAsync("/api/pages/architecture/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal("# The four layers", (await restored.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("body").GetString());
        await Refusals.Problem(
            await admin.PostAsync("/api/pages/architecture/restore", null, Ct),
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
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Architecture", body = "Dependencies point inward and only inward." }, Ct);
        await admin.PostAsJsonAsync("/api/pages", new { slug = "onboarding", title = "Onboarding", body = "Start with docker compose up." }, Ct);

        Assert.Equal(["architecture"], await Found(admin, "inward"));
        Assert.Equal(["architecture"], await Found(admin, "Architecture"));
        Assert.Equal(["onboarding"], await Found(admin, "\"docker compose\""));

        // The `simple` configuration, so an identifier survives being searched for.
        Assert.Equal(["onboarding"], await Found(admin, "-inward compose"));
        Assert.Empty(await Found(admin, "nothing here"));

        // A filter, not a ranking: the order stays the slug's.
        Assert.Equal(["architecture", "onboarding"], await Found(admin, "docker OR inward"));

        // A deleted page is not found while it is in its grace period (ADR 0013).
        await admin.DeleteAsync("/api/pages/architecture", Ct);
        Assert.Empty(await Found(admin, "inward"));
    }

    /// <summary>
    /// One instance holds one team's infrastructure, and every user sees
    /// everything in it (VISION 9): there is no scope to be outside of, and a
    /// user nobody granted anything reads and writes the wiki like the rest.
    /// </summary>
    [Fact]
    public async Task Every_user_sees_the_whole_wiki_and_writes_to_it()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/api/pages", new { slug = "architecture", title = "Architecture" }, Ct);

        using var somebody = instance.ClientWith(await instance.AddActiveUserAsync("somebody"));

        Assert.Equal(
            ["architecture"],
            (await somebody.GetFromJsonAsync<JsonElement>("/api/pages", Ct))
                .EnumerateArray().Select(p => p.GetProperty("slug").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await somebody.GetAsync("/api/pages/architecture", Ct)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            (await somebody.PostAsJsonAsync("/api/pages", new { slug = "onboarding", title = "Onboarding" }, Ct)).StatusCode);
    }

    private static async Task<string[]> Found(HttpClient client, string query) =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"/api/pages?q={Uri.EscapeDataString(query)}", Ct))
            .EnumerateArray().Select(p => p.GetProperty("slug").GetString()!)];

    private static async Task<HttpClient> Agent(AnInstance instance, HttpClient admin, string name)
    {
        using var created = await admin.PostAsJsonAsync("/api/agents", new { name }, Ct);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }
}
