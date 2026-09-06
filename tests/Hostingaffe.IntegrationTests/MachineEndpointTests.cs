using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Machines over HTTP (<c>docs/api.md</c>, Machines): the first record of the
/// product itself, open to agents like everything else that is content
/// (planaffe ADR 0015).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MachineEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_records_a_machine_and_reads_it_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var agent = await Agent(instance, admin, "one");

        using var created = await agent.PostAsJsonAsync(
            "/api/machines",
            new
            {
                key = "ex44",
                name = "The big one",
                hostname = "ex44",
                kind = "dedicated",
                provider = "hetzner",
                plan = "EX44",
                location = "fsn1-dc14",
                os = "Ubuntu 26.04 LTS",
                arch = "amd64",
                cpu = "Intel i5-13500",
                memory = "64G",
                disk = "2×512G NVMe ZFS mirror",
                ipv4 = "192.0.2.10",
                ipv6 = "2001:db8::1",
                private_ip = "198.51.100.7",
                ssh = "ex44",
                status = "active",
                measured_at = "2026-09-01T08:00:00Z",
                description = "The box everything else sits on.",
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/machines/ex44", created.Headers.Location?.ToString());

        var machine = await agent.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        Assert.Equal("ex44", machine.GetProperty("key").GetString());
        Assert.Equal("The big one", machine.GetProperty("name").GetString());
        Assert.Equal("dedicated", machine.GetProperty("kind").GetString());
        Assert.Equal("amd64", machine.GetProperty("arch").GetString());
        Assert.Equal("2×512G NVMe ZFS mirror", machine.GetProperty("disk").GetString());
        Assert.Equal("198.51.100.7", machine.GetProperty("private_ip").GetString());
        Assert.Equal(JsonValueKind.Null, machine.GetProperty("host").ValueKind);
        Assert.Equal("one", machine.GetProperty("created_by").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_machine_without_a_name_is_called_by_its_key_and_the_list_is_slim()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Machine(admin, "ex44", "dedicated");
        await Machine(admin, "cx22", "vps", new { status = "planned" });

        var machines = await admin.GetFromJsonAsync<JsonElement>("/api/machines", Ct);
        Assert.Equal(["cx22", "ex44"], machines.EnumerateArray().Select(m => m.GetProperty("key").GetString()));
        Assert.Equal("cx22", machines[0].GetProperty("name").GetString());

        // Slim is slim: what a person reads down a column, and not the description.
        Assert.False(machines[0].TryGetProperty("description", out _));

        var planned = await admin.GetFromJsonAsync<JsonElement>("/api/machines?status=planned", Ct);
        Assert.Equal(["cx22"], planned.EnumerateArray().Select(m => m.GetProperty("key").GetString()));

        var dedicated = await admin.GetFromJsonAsync<JsonElement>("/api/machines?kind=dedicated", Ct);
        Assert.Equal(["ex44"], dedicated.EnumerateArray().Select(m => m.GetProperty("key").GetString()));
    }

    [Fact]
    public async Task Only_a_vm_runs_on_a_machine()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Machine(admin, "ex44", "dedicated");

        using var refused = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "cx22", kind = "vps", host = "ex44" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("host", (await Problem(refused)).GetProperty("errors").EnumerateObject().Single().Name);

        using var accepted = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "docker-prod-01", kind = "vm", host = "ex44" }, Ct);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        var vm = await admin.GetFromJsonAsync<JsonElement>("/api/machines/docker-prod-01", Ct);
        Assert.Equal("ex44", vm.GetProperty("host").GetString());
    }

    [Fact]
    public async Task A_host_that_would_close_the_chain_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Machine(admin, "ex44", "dedicated");
        await Machine(admin, "outer", "vm", new { host = "ex44" });
        await Machine(admin, "inner", "vm", new { host = "outer" });

        // `outer` already runs on `ex44` by way of nothing, but `inner` runs on
        // `outer`: making `inner` the host of `outer` would close the ring.
        using var refused = await admin.PatchAsJsonAsync("/api/machines/outer", new { host = "inner" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using var itself = await admin.PatchAsJsonAsync("/api/machines/outer", new { host = "outer" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, itself.StatusCode);
    }

    [Fact]
    public async Task A_machine_that_stops_being_a_vm_stops_having_a_host()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Machine(admin, "ex44", "dedicated");
        await Machine(admin, "docker-prod-01", "vm", new { host = "ex44" });

        using var moved = await admin.PatchAsJsonAsync("/api/machines/docker-prod-01", new { kind = "local" }, Ct);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var machine = await moved.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("local", machine.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, machine.GetProperty("host").ValueKind);
    }

    [Fact]
    public async Task A_field_left_out_stays_and_the_empty_string_clears_it()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Machine(admin, "ex44", "dedicated", new { provider = "hetzner", location = "fsn1-dc14" });

        using var changed = await admin.PatchAsJsonAsync(
            "/api/machines/ex44", new { location = "", os = "Ubuntu 26.04 LTS" }, Ct);

        var machine = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("hetzner", machine.GetProperty("provider").GetString());
        Assert.Equal(JsonValueKind.Null, machine.GetProperty("location").ValueKind);
        Assert.Equal("Ubuntu 26.04 LTS", machine.GetProperty("os").GetString());
    }

    [Fact]
    public async Task The_key_is_immutable_and_taken_once()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Machine(admin, "ex44", "dedicated");

        using var twice = await admin.PostAsJsonAsync("/api/machines", new { key = "ex44", kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);

        // A key in a change body is a field the object does not define.
        using var renamed = await admin.PatchAsJsonAsync("/api/machines/ex44", new { key = "ex45" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, renamed.StatusCode);
        Assert.Equal("unknown-field", (await Problem(renamed)).GetProperty("type").GetString()?.Split('/')[^1]);
    }

    [Theory]
    [InlineData("kind", "toaster")]
    [InlineData("arch", "sparc")]
    [InlineData("status", "broken")]
    public async Task A_value_outside_a_closed_set_is_refused_at_the_door(string field, string value)
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        var body = new Dictionary<string, string> { ["key"] = "ex44", ["kind"] = "dedicated", [field] = value };

        using var refused = await admin.PostAsJsonAsync("/api/machines", body, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_filter_spells_its_value_the_way_the_contract_does()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Machine(admin, "ex44", "dedicated");

        // The CLR name is not the spelling: `Active` is not what the contract
        // writes, and a word outside the set is a refusal, not an empty list.
        using var clrName = await admin.GetAsync("/api/machines?status=Active", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, clrName.StatusCode);

        using var outside = await admin.GetAsync("/api/machines?kind=toaster", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Equal("kind", (await Problem(outside)).GetProperty("errors").EnumerateObject().Single().Name);

        // An empty filter is no filter.
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/machines?status=", Ct);
        Assert.Single(all.EnumerateArray());
    }

    [Fact]
    public async Task A_refusal_says_what_is_wrong_without_naming_a_clr_type()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var refused = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "ex44", kind = "toaster" }, Ct);

        var detail = (await Problem(refused)).GetProperty("detail").GetString()!;
        Assert.DoesNotContain("Hostingaffe.", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_machine_needs_a_key_and_a_kind()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var noKind = await admin.PostAsJsonAsync("/api/machines", new { key = "ex44" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noKind.StatusCode);
        Assert.Equal("kind", (await Problem(noKind)).GetProperty("errors").EnumerateObject().Single().Name);

        using var noKey = await admin.PostAsJsonAsync("/api/machines", new { kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal("key", (await Problem(noKey)).GetProperty("errors").EnumerateObject().Single().Name);
    }

    [Fact]
    public async Task An_address_that_is_not_an_address_is_refused_and_names_its_field()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var refused = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "ex44", kind = "vps", ipv4 = "2001:db8::1" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("ipv4", (await Problem(refused)).GetProperty("errors").EnumerateObject().Single().Name);
    }

    [Fact]
    public async Task The_history_carries_every_change_and_the_description_without_its_text()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Machine(admin, "ex44", "dedicated");
        await admin.PatchAsJsonAsync(
            "/api/machines/ex44",
            new { provider = "hetzner", status = "retired", description = "Gone to the scrapyard." },
            Ct);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/history", Ct);
        var fields = history.EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToArray();

        Assert.Equal("created", fields[0]);
        Assert.Contains("provider", fields);
        Assert.Contains("status", fields);
        Assert.Contains("description", fields);

        var status = history.EnumerateArray().Single(e => e.GetProperty("field").GetString() == "status");
        Assert.Equal("active", status.GetProperty("old_value").GetString());
        Assert.Equal("retired", status.GetProperty("new_value").GetString());

        // A text records that it changed, not how.
        var description = history.EnumerateArray().Single(e => e.GetProperty("field").GetString() == "description");
        Assert.Equal(JsonValueKind.Null, description.GetProperty("new_value").ValueKind);

        Assert.Equal("maintainer", history[0].GetProperty("actor").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_change_over_somebody_elses_is_stale()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        var created = await Machine(admin, "ex44", "dedicated");
        var read = created.GetProperty("updated_at").GetString();

        await admin.PatchAsJsonAsync("/api/machines/ex44", new { provider = "hetzner" }, Ct);

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/machines/ex44")
        {
            Content = JsonContent.Create(new { provider = "netcup" }),
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

        using var missing = await admin.GetAsync("/api/machines/nowhere", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // Not a key at all is still not found: it arrived in the path.
        using var nonsense = await admin.GetAsync("/api/machines/Not%20A%20Key", Ct);
        Assert.Equal(HttpStatusCode.NotFound, nonsense.StatusCode);
    }

    private static async Task<JsonElement> Machine(HttpClient client, string key, string kind, object? rest = null)
    {
        var body = new Dictionary<string, object?> { ["key"] = key, ["kind"] = kind };
        foreach (var property in rest?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(rest);
        }

        using var created = await client.PostAsJsonAsync("/api/machines", body, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<HttpClient> Agent(AnInstance instance, HttpClient admin, string name)
    {
        using var created = await admin.PostAsJsonAsync("/api/agents", new { name }, Ct);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }
}
