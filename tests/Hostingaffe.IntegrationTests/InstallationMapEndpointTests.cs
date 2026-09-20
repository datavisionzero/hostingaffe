using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class InstallationMapEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_read_includes_urls_and_orders_by_latest_deployment_time()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var anonymous = instance.ClientWith("");
        using var denied = await anonymous.GetAsync("/api/machines/host/installation-map", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var host = await admin.PostAsJsonAsync("/api/machines", new { key = "host", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, host.StatusCode);
        using var other = await admin.PostAsJsonAsync("/api/machines", new { key = "other", kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        using var software = await admin.PostAsJsonAsync("/api/software", new { key = "site" }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);

        var empty = await admin.GetFromJsonAsync<JsonElement>("/api/machines/host/installation-map", Ct);
        Assert.Equal("host", empty.GetProperty("machine").GetString());
        Assert.Empty(empty.GetProperty("installations").EnumerateArray());

        await Add(admin, "older", "host", "application", ["https://one.example.test/a", "https://two.example.test/"], "2026-01-01T10:00:00Z");
        await Add(admin, "newer", "host", "application", ["https://new.example.test/"], "2026-06-01T10:00:00Z");
        await Add(admin, "platform", "host", "platform", [], null, "retired");
        await Add(admin, "elsewhere", "other", "application", [], null);
        using var backfilled = await admin.PostAsJsonAsync("/api/installations/older/deployments",
            new { version = "0.9", at = "2025-01-01T10:00:00Z" }, Ct);
        Assert.Equal(HttpStatusCode.Created, backfilled.StatusCode);

        var map = await admin.GetFromJsonAsync<JsonElement>("/api/machines/host/installation-map", Ct);
        var entries = map.GetProperty("installations").EnumerateArray().ToArray();
        Assert.Equal(["newer", "older", "platform"], entries.Select(entry => entry.GetProperty("key").GetString()));
        Assert.Equal("site", entries[0].GetProperty("software").GetString());
        Assert.Equal("application", entries[0].GetProperty("role").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T10:00:00Z"), entries[0].GetProperty("latest_deployment_at").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T10:00:00Z"), entries[1].GetProperty("latest_deployment_at").GetDateTimeOffset());
        Assert.Equal(["https://one.example.test/a", "https://two.example.test/"],
            entries[1].GetProperty("urls").EnumerateArray().Select(url => url.GetString()));
        Assert.Equal("platform", entries[2].GetProperty("role").GetString());
        Assert.Equal("retired", entries[2].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, entries[2].GetProperty("latest_deployment_at").ValueKind);

        using var retired = await admin.PatchAsJsonAsync("/api/machines/host", new { status = "retired" }, Ct);
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var still = await admin.GetFromJsonAsync<JsonElement>("/api/machines/host/installation-map", Ct);
        Assert.Equal("retired", still.GetProperty("status").GetString());
        Assert.Equal(3, still.GetProperty("installations").GetArrayLength());

        using var missing = await admin.GetAsync("/api/machines/missing/installation-map", Ct);
        await Refusals.Problem(missing, HttpStatusCode.NotFound, "not-found");
    }

    private static async Task Add(HttpClient client, string key, string machine, string role, string[] urls, string? at, string status = "active")
    {
        using var created = await client.PostAsJsonAsync("/api/installations", new
        {
            key, machine, software = "site", environment = "production", role, urls, status,
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        if (at is null) return;
        using var recorded = await client.PostAsJsonAsync($"/api/installations/{key}/deployments",
            new { version = "1.0", at }, Ct);
        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
    }
}
