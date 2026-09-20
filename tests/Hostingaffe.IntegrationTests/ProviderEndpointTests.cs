using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class ProviderEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Providers_are_authored_changed_found_and_restored_like_other_records()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var anonymous = instance.ClientWith("");
        using var denied = await anonymous.GetAsync("/api/providers", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var created = await admin.PostAsJsonAsync("/api/providers?note=first",
            new { key = "example-host", name = "Example Host", description = "The first notes." }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/providers/example-host", created.Headers.Location?.ToString());

        var read = await admin.GetFromJsonAsync<JsonElement>("/api/providers/example-host", Ct);
        Assert.Equal("The first notes.", read.GetProperty("description").GetString());
        var version = read.GetProperty("updated_at").GetString();

        using var changed = await admin.PatchAsJsonAsync("/api/providers/example-host",
            new { name = "Example Hosting", description = "Revised notes." }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        using var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/providers/example-host")
        {
            Content = JsonContent.Create(new { name = "Old name" }),
        };
        stale.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        using var refused = await admin.SendAsync(stale, Ct);
        await Refusals.Problem(refused, HttpStatusCode.PreconditionFailed, "stale");

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/providers/example-host/history", Ct);
        Assert.Equal(["created", "name", "description"],
            history.EnumerateArray().Select(entry => entry.GetProperty("field").GetString()));
        Assert.Equal(JsonValueKind.Null, history[2].GetProperty("new_value").ValueKind);

        var hits = await admin.GetFromJsonAsync<JsonElement>("/api/search?q=Revised", Ct);
        Assert.Contains(hits.EnumerateArray(), hit =>
            hit.GetProperty("kind").GetString() == "provider" && hit.GetProperty("key").GetString() == "example-host");

        using var deleted = await admin.DeleteAsync("/api/providers/example-host", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await admin.GetAsync("/api/providers/example-host", Ct);
        await Refusals.Problem(missing, HttpStatusCode.NotFound, "deleted");
        using var restored = await admin.PostAsync("/api/providers/example-host/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public async Task Machines_use_live_provider_keys_and_vms_inherit_through_their_hosts()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var provider = await admin.PostAsJsonAsync("/api/providers", new { key = "example-host" }, Ct);
        Assert.Equal(HttpStatusCode.Created, provider.StatusCode);

        using var invalid = await admin.PostAsJsonAsync("/api/machines",
            new { key = "bad", kind = "vps", provider = "missing" }, Ct);
        await Refusals.Problem(invalid, HttpStatusCode.BadRequest, "validation");

        using var host = await admin.PostAsJsonAsync("/api/machines",
            new { key = "host", kind = "vps", provider = "example-host" }, Ct);
        Assert.Equal(HttpStatusCode.Created, host.StatusCode);
        using var guest = await admin.PostAsJsonAsync("/api/machines",
            new { key = "guest", kind = "vm", host = "host" }, Ct);
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
        using var independent = await admin.PostAsJsonAsync("/api/machines",
            new { key = "another", kind = "vm", host = "host", provider = "example-host" }, Ct);
        await Refusals.Problem(independent, HttpStatusCode.BadRequest, "validation");

        var vm = await admin.GetFromJsonAsync<JsonElement>("/api/machines/guest", Ct);
        Assert.Equal("example-host", vm.GetProperty("provider").GetString());
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/machines?provider=example-host", Ct);
        Assert.Equal(["guest", "host"], list.EnumerateArray().Select(row => row.GetProperty("key").GetString()));

        using var blocked = await admin.DeleteAsync("/api/providers/example-host", Ct);
        await Refusals.Problem(blocked, HttpStatusCode.UnprocessableEntity, "transition");

        using var cleared = await admin.PatchAsJsonAsync("/api/machines/host",
            new { provider = "" }, Ct);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        vm = await admin.GetFromJsonAsync<JsonElement>("/api/machines/guest", Ct);
        Assert.Equal(JsonValueKind.Null, vm.GetProperty("provider").ValueKind);

        using var removed = await admin.DeleteAsync("/api/providers/example-host", Ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var deletedRef = await admin.PatchAsJsonAsync("/api/machines/host",
            new { provider = "example-host" }, Ct);
        await Refusals.Problem(deletedRef, HttpStatusCode.BadRequest, "validation");
    }
}
