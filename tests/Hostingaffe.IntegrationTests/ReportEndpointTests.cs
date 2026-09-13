using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Handing in a report over HTTP (<c>docs/api.md</c>, Reports): the one write a
/// machine makes, behind the one door a machine token opens (ADR 0015,
/// ADR 0016).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReportEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_machine_hands_in_a_report_and_changes_nothing_about_itself()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        using var handed = await machine.PostAsJsonAsync("/api/machines/ex44/reports", AReport(), Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);
        Assert.Equal("/api/machines/ex44/reports/1", handed.Headers.Location?.ToString());

        var receipt = await handed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, receipt.GetProperty("number").GetInt32());
        Assert.True(receipt.GetProperty("received_at").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));

        // A report is a sample beside the record: nothing of the machine moved,
        // and the history says nothing happened (ADR 0015).
        var read = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        Assert.Equal("planned", read.GetProperty("status").GetString());
        Assert.False(read.TryGetProperty("os", out var os) && os.ValueKind is JsonValueKind.String);

        // The row the machine was created with, and nothing the report added.
        var history = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/history", Ct);
        Assert.Equal(["created"], history.EnumerateArray().Select(entry => entry.GetProperty("field").GetString()));
    }

    [Fact]
    public async Task A_machine_token_for_another_machine_finds_nothing()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");
        await Machine(admin, "cx22");

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("cx22"));

        // The answer does not tell "there is no such machine" from "that one is
        // not yours": a token that could enumerate keys would read something.
        await Refusals.Problem(
            await machine.PostAsJsonAsync("/api/machines/ex44/reports", AReport(), Ct),
            HttpStatusCode.NotFound,
            "not-found");

        await Refusals.Problem(
            await machine.PostAsJsonAsync("/api/machines/nowhere/reports", AReport(), Ct),
            HttpStatusCode.NotFound,
            "not-found");
    }

    [Fact]
    public async Task A_user_token_and_an_agent_token_are_not_a_machine()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");
        await instance.AddMachineTokenAsync("ex44");

        using var created = await admin.PostAsJsonAsync("/api/agents", new { name = "one" }, Ct);
        using var agent = instance.ClientWith(
            (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());

        await Refusals.Problem(
            await admin.PostAsJsonAsync("/api/machines/ex44/reports", AReport(), Ct),
            HttpStatusCode.Unauthorized,
            "unauthenticated");

        await Refusals.Problem(
            await agent.PostAsJsonAsync("/api/machines/ex44/reports", AReport(), Ct),
            HttpStatusCode.Unauthorized,
            "unauthenticated");

        using var none = instance.ClientWith(null);
        await Refusals.Problem(
            await none.PostAsJsonAsync("/api/machines/ex44/reports", AReport(), Ct),
            HttpStatusCode.Unauthorized,
            "unauthenticated");
    }

    [Fact]
    public async Task A_field_the_contract_does_not_know_is_refused_rather_than_ignored()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        var outer = await Refusals.Problem(
            await machine.PostAsJsonAsync(
                "/api/machines/ex44/reports",
                new { collected_at = "2026-09-13T08:00:00Z", temperature = 41 },
                Ct),
            HttpStatusCode.BadRequest,
            "unknown-field");
        Assert.Equal("temperature", outer.GetProperty("field").GetString());

        // Closed section by section, which is what keeps the body from becoming
        // a collecting bin.
        var inner = await Refusals.Problem(
            await machine.PostAsJsonAsync(
                "/api/machines/ex44/reports",
                new
                {
                    collected_at = "2026-09-13T08:00:00Z",
                    containers = new[] { new { name = "logaffe", environment = "SECRET=1" } },
                },
                Ct),
            HttpStatusCode.BadRequest,
            "unknown-field");
        Assert.Equal("environment", inner.GetProperty("field").GetString());
    }

    [Fact]
    public async Task A_body_over_sixty_four_kilobytes_is_refused_at_the_door()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        var huge = JsonSerializer.Serialize(new
        {
            collected_at = "2026-09-13T08:00:00Z",
            host = new { hostname = "ex44" },
            missing = new[] { new { section = "containers", reason = new string('x', 70_000) } },
        });

        using var body = new StringContent(huge, Encoding.UTF8, "application/json");
        var problem = await Refusals.Problem(
            await machine.PostAsync("/api/machines/ex44/reports", body, Ct),
            HttpStatusCode.RequestEntityTooLarge,
            "too-large");

        Assert.Equal(64 * 1024, problem.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task A_second_report_within_the_minute_is_told_to_come_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        using var first = await machine.PostAsJsonAsync("/api/machines/ex44/reports", AReport(), Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var problem = await Refusals.Problem(
            await machine.PostAsJsonAsync("/api/machines/ex44/reports", AReport(), Ct),
            HttpStatusCode.TooManyRequests,
            "rate-limited");

        Assert.InRange(problem.GetProperty("retry_after").GetInt32(), 1, 60);
    }

    [Fact]
    public async Task A_host_without_docker_reports_what_it_has_and_says_what_it_could_not()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        using var handed = await machine.PostAsJsonAsync(
            "/api/machines/ex44/reports",
            new
            {
                collected_at = "2026-09-13T08:00:00Z",
                agent = "0.4.0",
                host = new { hostname = "ex44", uptime_seconds = 100 },
                missing = new[] { new { section = "containers", reason = "docker is not installed" } },
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);
    }

    [Fact]
    public async Task A_size_that_is_not_a_number_of_bytes_is_refused_by_the_field_it_arrived_in()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        await Refusals.Problem(
            await machine.PostAsJsonAsync(
                "/api/machines/ex44/reports",
                new
                {
                    collected_at = "2026-09-13T08:00:00Z",
                    disks = new[] { new { mount = "/", size_bytes = "42G" } },
                },
                Ct),
            HttpStatusCode.BadRequest,
            "validation");

        var negative = await Refusals.Problem(
            await machine.PostAsJsonAsync(
                "/api/machines/ex44/reports",
                new
                {
                    collected_at = "2026-09-13T08:00:00Z",
                    disks = new[] { new { mount = "/", percent = 140 } },
                },
                Ct),
            HttpStatusCode.BadRequest,
            "validation");

        Assert.True(negative.GetProperty("errors").TryGetProperty("disks.percent", out _));
    }

    /// <summary>
    /// A clock far in the future is stored rather than refused: refusing it
    /// would deny a machine with a wrong clock its sign of life (ADR 0015).
    /// </summary>
    [Fact]
    public async Task A_host_with_a_wrong_clock_is_still_heard()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Machine(admin, "ex44");
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));

        using var handed = await machine.PostAsJsonAsync(
            "/api/machines/ex44/reports",
            new { collected_at = "2099-01-01T00:00:00Z", host = new { hostname = "ex44" } },
            Ct);

        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);
    }

    internal static object AReport() => new
    {
        collected_at = "2026-09-13T08:00:07Z",
        agent = "0.4.0",
        host = new
        {
            hostname = "ex44",
            os = "Ubuntu 26.04 LTS",
            kernel = "6.14.0-27-generic",
            arch = "x86_64",
            uptime_seconds = 1_893_244,
            load1 = 0.14,
            load5 = 0.2,
            load15 = 0.18,
        },
        memory = new
        {
            total_bytes = 67_430_400_000L,
            used_bytes = 19_204_000_000L,
            available_bytes = 46_900_000_000L,
            swap_total_bytes = 0,
            swap_used_bytes = 0,
        },
        disks = new[]
        {
            new { mount = "/", device = "/dev/nvme0n1p2", size_bytes = 502_000_000_000L, used_bytes = 301_000_000_000L, percent = 60 },
        },
        containers = new[]
        {
            new
            {
                name = "logaffe",
                image = "ghcr.io/datavisionzero/logaffe:1.4.0",
                state = "running",
                status = "Up 3 days",
                health = "healthy",
                restarts = 0,
                started_at = "2026-09-10T09:12:00Z",
                ports = new[] { "127.0.0.1:18502->8080/tcp" },
            },
        },
        missing = Array.Empty<object>(),
    };

    internal static async Task Machine(HttpClient admin, string key)
    {
        using var created = await admin.PostAsJsonAsync(
            "/api/machines", new { key, kind = "dedicated", status = "planned" }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }
}
