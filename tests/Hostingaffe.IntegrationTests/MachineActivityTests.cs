using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// What a list of machines says per row about what has been going on
/// (<c>docs/api.md</c>, Machines): asked for, never served by default.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MachineActivityTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_the_parameter_the_list_is_what_it_was()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHost(admin);

        var rows = await admin.GetFromJsonAsync<JsonElement>("/api/machines", Ct);
        var row = rows.EnumerateArray().Single();

        Assert.Equal(JsonValueKind.Null, row.GetProperty("activity").ValueKind);
    }

    [Fact]
    public async Task The_window_carries_the_counts_and_the_newest_deployment()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHost(admin);

        using var deployed = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/deployments", new { version = "1.5.0" }, Ct);
        Assert.Equal(HttpStatusCode.Created, deployed.StatusCode);

        var activity = await ActivityOf(admin, "7d");

        Assert.Equal("7d", activity.GetProperty("window").GetString());
        // The machine and the installation were born in the window; the
        // software belongs to no machine and is in nobody's count. The two
        // deployments are the installation's first and the one just recorded.
        Assert.Equal(2, activity.GetProperty("changes").GetInt32());
        Assert.Equal(2, activity.GetProperty("deployments").GetInt32());
        Assert.Equal(1, activity.GetProperty("installations").GetInt32());
        Assert.Equal(0, activity.GetProperty("drift").GetInt32());

        var latest = activity.GetProperty("latest");
        Assert.Equal("logaffe-prod", latest.GetProperty("installation").GetString());
        Assert.Equal(2, latest.GetProperty("number").GetInt32());
        Assert.Equal("1.5.0", latest.GetProperty("version").GetString());
        Assert.Equal("1.4.0", latest.GetProperty("previous").GetString());
    }

    /// <summary>
    /// The numbers are the reading's own, counted rather than listed. A count
    /// the screen beside it contradicts would be worse than no count at all.
    /// </summary>
    [Fact]
    public async Task The_counts_are_what_the_reading_shows_in_the_same_window()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHost(admin);

        using var written = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/files",
            new { path = "compose.override.yml", content = "services: {}" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, written.StatusCode);

        var activity = await ActivityOf(admin, "7d");
        var events = (await admin.GetFromJsonAsync<JsonElement>("/api/history?machine=ex44&limit=200", Ct))
            .EnumerateArray()
            .ToArray();

        Assert.Equal(
            events.Count(one => one.GetProperty("subject_kind").GetString() == "deployment"),
            activity.GetProperty("deployments").GetInt32());
        Assert.Equal(
            events.Count(one => one.GetProperty("subject_kind").GetString() != "deployment"),
            activity.GetProperty("changes").GetInt32());
    }

    /// <summary>
    /// The one number here that costs: the latest report of every machine,
    /// read against that machine's installations.
    /// </summary>
    [Fact]
    public async Task The_drift_of_the_latest_report_is_counted()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHost(admin, image: "ghcr.io/datavisionzero/logaffe");

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        using var handed = await machine.PostAsJsonAsync(
            "/api/machines/ex44/reports",
            new
            {
                collected_at = "2026-09-13T08:00:00Z",
                containers = new[]
                {
                    new { name = "logaffe", image = "ghcr.io/datavisionzero/logaffe:1.3.2", state = "running", status = "…" },
                },
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);

        Assert.Equal(1, (await ActivityOf(admin, "24h")).GetProperty("drift").GetInt32());
    }

    [Fact]
    public async Task A_window_that_is_not_one_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Refusals.Problem(
            await admin.GetAsync("/api/machines?activity=lastweek", Ct), HttpStatusCode.BadRequest, "validation");

        // Ninety days is the furthest anyone reads back in a list.
        await Refusals.Problem(
            await admin.GetAsync("/api/machines?activity=365d", Ct), HttpStatusCode.BadRequest, "validation");
    }

    /// <summary>
    /// The measurement the ticket asks for: fifty machines, each with an
    /// installation, a deployment and a report, in one call. The bound here is
    /// generous on purpose — it is a regression guard on a shared machine, not
    /// the number. What the number was stands in the ticket.
    /// </summary>
    [Fact]
    public async Task Fifty_machines_are_one_call_and_not_fifty()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var software = await admin.PostAsJsonAsync("/api/software", new { key = "logaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);

        for (var number = 0; number < 50; number++)
        {
            var key = $"ex{number:D3}";

            using var machine = await admin.PostAsJsonAsync("/api/machines", new { key, kind = "vps" }, Ct);
            Assert.Equal(HttpStatusCode.Created, machine.StatusCode);

            using var installation = await admin.PostAsJsonAsync(
                "/api/installations",
                new
                {
                    key = $"logaffe-{key}",
                    machine = key,
                    software = "logaffe",
                    environment = "production",
                    role = "application",
                    version = "1.4.0",
                },
                Ct);
            Assert.Equal(HttpStatusCode.Created, installation.StatusCode);

            using var reporter = instance.ClientWith(await instance.AddMachineTokenAsync(key));
            using var handed = await reporter.PostAsJsonAsync(
                $"/api/machines/{key}/reports",
                new
                {
                    collected_at = "2026-09-13T08:00:00Z",
                    containers = new[] { new { name = "logaffe", image = "logaffe:1.4.0", state = "running", status = "…" } },
                },
                Ct);
            Assert.Equal(HttpStatusCode.Created, handed.StatusCode);
        }

        var clock = Stopwatch.StartNew();
        var rows = await admin.GetFromJsonAsync<JsonElement>("/api/machines?activity=7d", Ct);
        clock.Stop();

        Assert.Equal(50, rows.EnumerateArray().Count());
        Assert.All(
            rows.EnumerateArray(),
            row => Assert.Equal(1, row.GetProperty("activity").GetProperty("installations").GetInt32()));
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(5),
            $"fifty machines with their activity took {clock.ElapsedMilliseconds} ms");
    }

    private static async Task<JsonElement> ActivityOf(HttpClient admin, string window)
    {
        var rows = await admin.GetFromJsonAsync<JsonElement>($"/api/machines?activity={window}", Ct);

        return rows.EnumerateArray().Single(row => row.GetProperty("key").GetString() == "ex44")
            .GetProperty("activity");
    }

    private static async Task AHost(HttpClient admin, string? image = null)
    {
        using var machine = await admin.PostAsJsonAsync("/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, machine.StatusCode);

        using var software = await admin.PostAsJsonAsync("/api/software", new { key = "logaffe", image }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);

        using var installation = await admin.PostAsJsonAsync(
            "/api/installations",
            new
            {
                key = "logaffe-prod",
                machine = "ex44",
                software = "logaffe",
                environment = "production",
                role = "application",
                version = "1.4.0",
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, installation.StatusCode);
    }
}
