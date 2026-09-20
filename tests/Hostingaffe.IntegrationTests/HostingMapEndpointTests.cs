using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class HostingMapEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_authenticated_read_has_all_provider_machine_and_address_facts()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var anonymous = instance.ClientWith("");
        using var denied = await anonymous.GetAsync("/api/hosting-map", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var provider = await admin.PostAsJsonAsync("/api/providers", new { key = "example-host", name = "Example Host" }, Ct);
        Assert.Equal(HttpStatusCode.Created, provider.StatusCode);
        using var host = await admin.PostAsJsonAsync("/api/machines", new
        {
            key = "host", kind = "dedicated", provider = "example-host", ipv4 = "192.0.2.10",
            ipv6 = "2001:db8::10", private_ip = "198.51.100.10",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, host.StatusCode);
        using var guest = await admin.PostAsJsonAsync("/api/machines", new { key = "guest", kind = "vm", host = "host" }, Ct);
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
        using var local = await admin.PostAsJsonAsync("/api/machines", new { key = "local", kind = "local", status = "retired" }, Ct);
        Assert.Equal(HttpStatusCode.Created, local.StatusCode);
        using var gone = await admin.PostAsJsonAsync("/api/machines", new { key = "gone", kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.Created, gone.StatusCode);
        using var deleted = await admin.DeleteAsync("/api/machines/gone", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var map = await admin.GetFromJsonAsync<JsonElement>("/api/hosting-map", Ct);
        var providers = map.GetProperty("providers");
        Assert.Equal("example-host", Assert.Single(providers.EnumerateArray()).GetProperty("key").GetString());
        var machines = map.GetProperty("machines").EnumerateArray().ToDictionary(row => row.GetProperty("key").GetString()!);
        Assert.Equal(["guest", "host", "local"], machines.Keys);
        Assert.Equal("example-host", machines["host"].GetProperty("provider").GetString());
        Assert.Equal("example-host", machines["guest"].GetProperty("provider").GetString());
        Assert.Equal("192.0.2.10", machines["host"].GetProperty("ipv4").GetString());
        Assert.Equal("2001:db8::10", machines["host"].GetProperty("ipv6").GetString());
        Assert.Equal("198.51.100.10", machines["host"].GetProperty("private_ip").GetString());
        Assert.Equal("retired", machines["local"].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, machines["local"].GetProperty("provider").ValueKind);
    }
}
