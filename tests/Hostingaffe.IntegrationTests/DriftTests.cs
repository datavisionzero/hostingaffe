using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// What the record and the machine disagree about (<c>docs/api.md</c>, Drift;
/// ADR 0015). The point of keeping a report beside the record rather than in
/// it: because both sides are there, they can be compared.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DriftTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_record_says_one_version_and_the_machine_reports_another()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, image: "ghcr.io/datavisionzero/logaffe", version: "1.4.0");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Reports(machine, "ghcr.io/datavisionzero/logaffe:1.3.2", "running");

        var drift = await Drift(admin, "/api/machines/ex44/reports/latest");
        var one = drift.EnumerateArray().Single();

        Assert.Equal("version", one.GetProperty("kind").GetString());
        Assert.Equal("logaffe-prod", one.GetProperty("subject").GetString());
        Assert.Equal("1.4.0", one.GetProperty("record").GetString());
        Assert.Equal("1.3.2", one.GetProperty("reported").GetString());

        // Both sides carry their age: what the record says and since when, what
        // the machine says and when it said it.
        Assert.Equal(JsonValueKind.String, one.GetProperty("record_at").ValueKind);
        Assert.Equal(JsonValueKind.String, one.GetProperty("reported_at").ValueKind);

        // And it is at the machine too, so that `ha machine view` and the
        // machine screen say it where the fields it contradicts are read.
        var read = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        Assert.Single(read.GetProperty("drift").EnumerateArray());
    }

    [Fact]
    public async Task The_versions_agree_and_nothing_is_said()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, image: "ghcr.io/datavisionzero/logaffe", version: "1.4.0");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Reports(machine, "ghcr.io/datavisionzero/logaffe:1.4.0", "running");

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    [Fact]
    public async Task An_installation_the_record_calls_active_whose_container_is_not_running()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, image: "ghcr.io/datavisionzero/logaffe", version: "1.4.0");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Reports(machine, "ghcr.io/datavisionzero/logaffe:1.4.0", "exited");

        var one = (await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray().Single();
        Assert.Equal("container", one.GetProperty("kind").GetString());
        Assert.Equal("active", one.GetProperty("record").GetString());
        Assert.Equal("exited", one.GetProperty("reported").GetString());
    }

    /// <summary>
    /// Two installations of one software on one machine: the report is shown
    /// and nothing is claimed. A wrong sentence is worse than none.
    /// </summary>
    [Fact]
    public async Task An_ambiguous_assignment_claims_nothing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, image: "ghcr.io/datavisionzero/logaffe", version: "1.4.0");
        using var second = await admin.PostAsJsonAsync(
            "/api/installations",
            new { key = "logaffe-stage", machine = "ex44", software = "logaffe", environment = "staging", role = "application", version = "1.4.0" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Reports(machine, "ghcr.io/datavisionzero/logaffe:1.3.2", "running");

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    [Fact]
    public async Task A_software_without_an_image_and_an_installation_without_a_container_claim_nothing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        // No image on the software: there is nothing to match a container on.
        await AHost(admin, image: null, version: "1.4.0");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Reports(machine, "ghcr.io/datavisionzero/logaffe:1.3.2", "running");

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    [Fact]
    public async Task A_container_no_installation_answers_to_claims_nothing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, image: "ghcr.io/datavisionzero/logaffe", version: "1.4.0");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Reports(machine, "redis:7.4", "running");

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    /// <summary>
    /// VISION 15.1 word for word: the machine says one thing, the record says
    /// another, measured forty days ago.
    /// </summary>
    [Fact]
    public async Task The_facts_of_the_machine_against_what_the_host_says_it_is()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await admin.PostAsJsonAsync(
            "/api/machines",
            new { key = "ex44", kind = "dedicated", os = "Ubuntu 24.04 LTS", arch = "amd64", measured_at = "2026-08-04T08:00:00Z" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        using var handed = await machine.PostAsJsonAsync(
            "/api/machines/ex44/reports",
            new
            {
                collected_at = "2026-09-13T08:00:00Z",
                host = new { hostname = "ex44", os = "Ubuntu 26.04 LTS", arch = "aarch64" },
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);

        var drift = (await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray().ToList();
        Assert.Equal(2, drift.Count);

        var os = drift.Single(one => one.GetProperty("field").GetString() == "os");
        Assert.Equal("fact", os.GetProperty("kind").GetString());
        Assert.Equal("Ubuntu 24.04 LTS", os.GetProperty("record").GetString());
        Assert.Equal("Ubuntu 26.04 LTS", os.GetProperty("reported").GetString());

        // `aarch64` is what a host calls what the record calls `arm64`; the two
        // are compared through the words that mean the same thing.
        var arch = drift.Single(one => one.GetProperty("field").GetString() == "arch");
        Assert.Equal("amd64", arch.GetProperty("record").GetString());
        Assert.Equal("arm64", arch.GetProperty("reported").GetString());
    }

    [Fact]
    public async Task A_host_that_reports_the_same_architecture_by_another_name_is_no_drift()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "ex44", kind = "dedicated", arch = "amd64" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        using var handed = await machine.PostAsJsonAsync(
            "/api/machines/ex44/reports",
            new { collected_at = "2026-09-13T08:00:00Z", host = new { arch = "x86_64" } },
            Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    private static async Task<JsonElement> Drift(HttpClient client, string address) =>
        (await client.GetFromJsonAsync<JsonElement>(address, Ct)).GetProperty("drift");

    /// <summary>A machine, a software, and one installation of it running a version.</summary>
    private static async Task AHost(HttpClient admin, string? image, string version)
    {
        using var machine = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, machine.StatusCode);

        using var software = await admin.PostAsJsonAsync(
            "/api/software", new { key = "logaffe", name = "logaffe", image }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);

        using var installation = await admin.PostAsJsonAsync(
            "/api/installations",
            new { key = "logaffe-prod", machine = "ex44", software = "logaffe", environment = "production", role = "application", version },
            Ct);
        Assert.Equal(HttpStatusCode.Created, installation.StatusCode);
    }

    private static async Task Reports(HttpClient machine, string image, string state)
    {
        using var handed = await machine.PostAsJsonAsync(
            "/api/machines/ex44/reports",
            new
            {
                collected_at = "2026-09-13T08:00:00Z",
                containers = new[] { new { name = "logaffe", image, state, status = "…" } },
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);
    }
}
