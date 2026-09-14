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

    /// <summary>
    /// The question VISION 16 asks as "what listens on 18502", answered from
    /// what is the case rather than from what somebody typed.
    /// </summary>
    [Fact]
    public async Task The_record_says_a_port_is_listened_on_and_nothing_listens_there()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "public" }]);
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        // The machine has a socket open, but on another port entirely.
        await Listens(machine, [new { port = 22, protocol = "tcp", binding = "public" }]);

        var one = (await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray().Single();
        Assert.Equal("port", one.GetProperty("kind").GetString());
        Assert.Equal("logaffe-prod", one.GetProperty("subject").GetString());
        Assert.Equal("port 18502/tcp", one.GetProperty("field").GetString());
        Assert.Equal("public", one.GetProperty("record").GetString());
        Assert.Equal(JsonValueKind.Null, one.GetProperty("reported").ValueKind);
    }

    /// <summary>
    /// The case worth finding: the record says a port is reachable and the
    /// socket is bound to loopback alone.
    /// </summary>
    [Fact]
    public async Task The_record_says_reachable_and_the_socket_is_loopback_alone()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "private" }]);
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(machine, [new { port = 18502, protocol = "tcp", binding = "loopback" }]);

        var one = (await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray().Single();
        Assert.Equal("port", one.GetProperty("kind").GetString());

        // A scope is not a binding: `private` is a firewall's doing, which no
        // listening socket shows. What both `public` and `private` need is a
        // socket bound past loopback, and this one is not.
        Assert.Equal("private", one.GetProperty("record").GetString());
        Assert.Equal("loopback", one.GetProperty("reported").GetString());
    }

    [Fact]
    public async Task A_port_the_record_and_the_machine_agree_about_says_nothing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "public" }]);
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(machine, [new { port = 18502, protocol = "tcp", binding = "public" }]);

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    /// <summary>
    /// <c>internal</c> means the port never reaches the host at all, so hearing
    /// nothing about it is the agreement rather than a hole in the record.
    /// </summary>
    [Fact]
    public async Task An_internal_port_the_host_never_hears_is_no_drift()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 5432, protocol = "tcp", scope = "internal" }]);
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(machine, [new { port = 22, protocol = "tcp", binding = "public" }]);

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    /// <summary>And the other way round: what should never have reached the host is bound in public.</summary>
    [Fact]
    public async Task An_internal_port_bound_in_public_is_drift()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 5432, protocol = "tcp", scope = "internal" }]);
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(machine, [new { port = 5432, protocol = "tcp", binding = "public" }]);

        var one = (await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray().Single();
        Assert.Equal("port", one.GetProperty("kind").GetString());
        Assert.Equal("internal", one.GetProperty("record").GetString());
        Assert.Equal("public", one.GetProperty("reported").GetString());
    }

    /// <summary>
    /// A machine nobody keeps ports for is told nothing about undocumented
    /// ones: the record says nothing about what belongs to the machine itself,
    /// so a drift about port 22 could never be resolved by anybody, and a drift
    /// nobody can clear teaches people to stop reading the list. The section is
    /// shown whole instead, and the three comparisons against an installation's
    /// ports run either way.
    /// </summary>
    [Fact]
    public async Task A_machine_that_keeps_no_ports_is_told_nothing_about_undocumented_ones()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "public" }]);
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(
            machine,
            [
                new { port = 18502, protocol = "tcp", binding = "public" },
                new { port = 22, protocol = "tcp", binding = "public" },
                new { port = 53, protocol = "udp", binding = "public" },
            ]);

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());

        var report = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/reports/latest", Ct);
        Assert.Equal(3, report.GetProperty("listening").GetArrayLength());
    }

    /// <summary>
    /// The question a documentation of rented machines is kept for: what is
    /// reachable from outside that nobody wrote down. It is answerable once the
    /// machine keeps its own ports — SSH is written down at the machine — and
    /// then it is a drift somebody can clear, by writing the port down or by
    /// closing it.
    /// </summary>
    [Fact]
    public async Task A_machine_that_keeps_its_ports_hears_about_one_that_stands_in_no_record()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "public" }]);
        await Keeps(admin, [new { port = 22, protocol = "tcp", scope = "public" }]);

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(
            machine,
            [
                new { port = 18502, protocol = "tcp", binding = "public" },
                new { port = 22, protocol = "tcp", binding = "public" },
                new { port = 8080, protocol = "tcp", binding = "public" },
            ]);

        // 18502 is the installation's and 22 is the machine's; 8080 is nobody's.
        var one = (await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray().Single();
        Assert.Equal("port", one.GetProperty("kind").GetString());
        Assert.Equal("ex44", one.GetProperty("subject").GetString());
        Assert.Equal("port 8080/tcp", one.GetProperty("field").GetString());

        // The record says nothing, which is the whole finding — and there is no
        // line to date, so `record_at` is null with it.
        Assert.Equal(JsonValueKind.Null, one.GetProperty("record").ValueKind);
        Assert.Equal(JsonValueKind.Null, one.GetProperty("record_at").ValueKind);
        Assert.Equal("public", one.GetProperty("reported").GetString());
    }

    /// <summary>
    /// A socket on loopback alone reaches nothing off this machine, so it is
    /// not what "reachable from outside and written down nowhere" asks about. A
    /// record of what an operator rents is not a process list.
    /// </summary>
    [Fact]
    public async Task A_port_bound_to_loopback_alone_is_not_an_undocumented_one()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "public" }]);
        await Keeps(admin, [new { port = 22, protocol = "tcp", scope = "public" }]);

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(
            machine,
            [
                new { port = 18502, protocol = "tcp", binding = "public" },
                new { port = 22, protocol = "tcp", binding = "public" },
                new { port = 5432, protocol = "tcp", binding = "loopback" },
            ]);

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    /// <summary>
    /// The machine's own ports are compared like an installation's, because
    /// they are the same field: the record says SSH is reachable and nothing
    /// listens there.
    /// </summary>
    [Fact]
    public async Task The_record_says_the_machine_listens_on_a_port_and_nothing_listens_there()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", null);
        await Keeps(admin, [new { port = 22, protocol = "tcp", scope = "public" }]);

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(machine, [new { port = 2222, protocol = "tcp", binding = "public" }]);

        var drift = (await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray().ToArray();
        Assert.Equal(2, drift.Length);

        var silent = drift.Single(one => one.GetProperty("field").GetString() == "port 22/tcp");
        Assert.Equal("ex44", silent.GetProperty("subject").GetString());
        Assert.Equal("public", silent.GetProperty("record").GetString());
        Assert.Equal(JsonValueKind.Null, silent.GetProperty("reported").ValueKind);

        // And the one that answers instead stands in no record at all.
        var loud = drift.Single(one => one.GetProperty("field").GetString() == "port 2222/tcp");
        Assert.Equal(JsonValueKind.Null, loud.GetProperty("record").ValueKind);
        Assert.Equal("public", loud.GetProperty("reported").GetString());
    }

    /// <summary>
    /// A port a planned installation wrote down is not an undocumented one:
    /// somebody put it in the record, whatever the installation's state says.
    /// The planned installation is still not expected to be listening.
    /// </summary>
    [Fact]
    public async Task A_port_a_planned_installation_wrote_down_is_not_undocumented()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "public" }]);
        using var parked = await admin.PatchAsJsonAsync("/api/installations/logaffe-prod", new { status = "planned" }, Ct);
        Assert.Equal(HttpStatusCode.OK, parked.StatusCode);

        await Keeps(admin, [new { port = 22, protocol = "tcp", scope = "public" }]);

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(
            machine,
            [
                new { port = 18502, protocol = "tcp", binding = "public" },
                new { port = 22, protocol = "tcp", binding = "public" },
            ]);

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    /// <summary>
    /// An installation the record does not call <c>active</c> is passed over: a
    /// planned one is not supposed to be listening, and saying so every quarter
    /// of an hour would be noise rather than drift.
    /// </summary>
    [Fact]
    public async Task A_planned_installation_is_not_expected_to_be_listening()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "public" }]);
        using var parked = await admin.PatchAsJsonAsync("/api/installations/logaffe-prod", new { status = "planned" }, Ct);
        Assert.Equal(HttpStatusCode.OK, parked.StatusCode);

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Listens(machine, [new { port = 22, protocol = "tcp", binding = "public" }]);

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    /// <summary>A report with no listening section at all claims nothing about ports.</summary>
    [Fact]
    public async Task A_report_that_says_nothing_about_listening_claims_nothing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await AHost(admin, "ghcr.io/datavisionzero/logaffe", "1.4.0", [new { port = 18502, protocol = "tcp", scope = "public" }]);
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        await Reports(machine, "ghcr.io/datavisionzero/logaffe:1.4.0", "running");

        Assert.Empty((await Drift(admin, "/api/machines/ex44/reports/latest")).EnumerateArray());
    }

    /// <summary>The ports the machine itself keeps: SSH, and what no installation answers to.</summary>
    private static async Task Keeps(HttpClient admin, object[] ports)
    {
        using var written = await admin.PatchAsJsonAsync("/api/machines/ex44", new { ports }, Ct);
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
    }

    private static async Task<JsonElement> Drift(HttpClient client, string address) =>
        (await client.GetFromJsonAsync<JsonElement>(address, Ct)).GetProperty("drift");

    /// <summary>A machine, a software, and one installation of it running a version.</summary>
    private static Task AHost(HttpClient admin, string? image, string version) => AHost(admin, image, version, null);

    /// <inheritdoc cref="AHost(HttpClient, string?, string)"/>
    private static async Task AHost(HttpClient admin, string? image, string version, object[]? ports)
    {
        using var machine = await admin.PostAsJsonAsync(
            "/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, machine.StatusCode);

        using var software = await admin.PostAsJsonAsync(
            "/api/software", new { key = "logaffe", name = "logaffe", image }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);

        using var installation = await admin.PostAsJsonAsync(
            "/api/installations",
            new { key = "logaffe-prod", machine = "ex44", software = "logaffe", environment = "production", role = "application", version, ports },
            Ct);
        Assert.Equal(HttpStatusCode.Created, installation.StatusCode);
    }

    /// <summary>
    /// What the machine has a socket open for, and nothing else. One per test:
    /// the door takes one report per machine per minute.
    /// </summary>
    private static async Task Listens(HttpClient machine, object[] listening)
    {
        using var handed = await machine.PostAsJsonAsync(
            "/api/machines/ex44/reports",
            new { collected_at = "2026-09-13T08:00:00Z", listening },
            Ct);

        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);
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
