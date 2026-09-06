using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Software over HTTP (<c>docs/api.md</c>, Software): the collection at
/// <c>/api/software</c>, because the word is uncountable and there is no
/// <c>/api/softwares</c> (<c>CONTEXT.md</c>).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SoftwareEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_records_a_software_and_reads_it_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var agent = await Agent(instance, admin, "one");

        using var created = await agent.PostAsJsonAsync(
            "/api/software",
            new
            {
                key = "logaffe",
                name = "logaffe",
                homepage = "https://example.test/logaffe",
                repository = "https://github.com/datavisionzero/logaffe",
                image = "ghcr.io/datavisionzero/logaffe",
                description = "Where the logs go.",
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/software/logaffe", created.Headers.Location?.ToString());

        var software = await agent.GetFromJsonAsync<JsonElement>("/api/software/logaffe", Ct);
        Assert.Equal("logaffe", software.GetProperty("key").GetString());
        Assert.Equal("ghcr.io/datavisionzero/logaffe", software.GetProperty("image").GetString());
        Assert.Equal("https://github.com/datavisionzero/logaffe", software.GetProperty("repository").GetString());
        Assert.Equal("one", software.GetProperty("created_by").GetProperty("name").GetString());

        // It carries no version, and no shape of it does.
        Assert.False(software.TryGetProperty("version", out _));
    }

    [Fact]
    public async Task A_software_without_a_name_is_called_by_its_key_and_the_list_is_slim()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Software(admin, "postgres");
        await Software(admin, "caddy", new { description = "The reverse proxy in front of everything." });

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/software", Ct);
        Assert.Equal(["caddy", "postgres"], list.EnumerateArray().Select(s => s.GetProperty("key").GetString()));
        Assert.Equal("caddy", list[0].GetProperty("name").GetString());
        Assert.False(list[0].TryGetProperty("description", out _));
    }

    [Fact]
    public async Task An_image_with_a_tag_is_refused_and_so_is_a_broken_url()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var tagged = await admin.PostAsJsonAsync(
            "/api/software", new { key = "caddy", image = "caddy:2" }, Ct);
        var problem = await Refusals.Problem(tagged, HttpStatusCode.BadRequest, "validation");
        Assert.Contains("image", problem.GetProperty("errors").EnumerateObject().Select(field => field.Name));

        using var digested = await admin.PostAsJsonAsync(
            "/api/software", new { key = "caddy", image = "caddy@sha256:abc" }, Ct);
        await Refusals.Problem(digested, HttpStatusCode.BadRequest, "validation");

        using var broken = await admin.PostAsJsonAsync(
            "/api/software", new { key = "caddy", homepage = "caddyserver.com" }, Ct);
        await Refusals.Problem(broken, HttpStatusCode.BadRequest, "validation");

        // None of the three left a row behind.
        using var missing = await admin.GetAsync("/api/software/caddy", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task The_key_is_immutable_and_a_version_is_a_field_a_software_does_not_have()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Software(admin, "caddy");

        using var renamed = await admin.PatchAsJsonAsync("/api/software/caddy", new { key = "caddy2" }, Ct);
        await Refusals.Problem(renamed, HttpStatusCode.BadRequest, "unknown-field");

        using var versioned = await admin.PatchAsJsonAsync("/api/software/caddy", new { version = "2.11.4" }, Ct);
        var problem = await Refusals.Problem(versioned, HttpStatusCode.BadRequest, "unknown-field");
        Assert.Contains("versions belong to deployments", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_that_is_taken_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Software(admin, "caddy");

        using var again = await admin.PostAsJsonAsync("/api/software", new { key = "caddy" }, Ct);
        await Refusals.Problem(again, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task The_history_carries_every_change_and_the_description_only_that_it_changed()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Software(admin, "caddy");

        using var changed = await admin.PatchAsJsonAsync(
            "/api/software/caddy",
            new { name = "Caddy", image = "caddy", description = "The reverse proxy." },
            Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/software/caddy/history", Ct);
        var fields = history.EnumerateArray().Select(entry => entry.GetProperty("field").GetString()!);

        Assert.Equal(["created", "name", "image", "description"], fields);

        var image = history.EnumerateArray().Single(entry => entry.GetProperty("field").GetString() == "image");
        Assert.Equal("caddy", image.GetProperty("new_value").GetString());

        var description = history.EnumerateArray().Single(entry => entry.GetProperty("field").GetString() == "description");
        Assert.Equal(JsonValueKind.Null, description.GetProperty("new_value").ValueKind);
    }

    [Fact]
    public async Task A_change_over_a_version_somebody_moved_is_stale()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        var created = await Software(admin, "caddy");
        var read = created.GetProperty("updated_at").GetString();

        using var moved = await admin.PatchAsJsonAsync("/api/software/caddy", new { name = "Caddy" }, Ct);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/software/caddy")
        {
            Content = JsonContent.Create(new { name = "Caddy 2" }),
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{read}\""));

        using var refused = await admin.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
    }

    [Fact]
    public async Task A_key_that_names_nothing_is_not_found()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var missing = await admin.GetAsync("/api/software/nowhere", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task<JsonElement> Software(HttpClient client, string key, object? rest = null)
    {
        var body = new Dictionary<string, object?> { ["key"] = key };
        foreach (var property in rest?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(rest);
        }

        using var created = await client.PostAsJsonAsync("/api/software", body, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<HttpClient> Agent(AnInstance instance, HttpClient admin, string name)
    {
        using var created = await admin.PostAsJsonAsync("/api/agents", new { name }, Ct);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }
}
