using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Reading reports over HTTP (<c>docs/api.md</c>, Reports): the series, the
/// latest, one by its number — and <c>last_seen</c> on the machine itself.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReportReadTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_machine_that_never_reported_has_an_empty_series_and_no_last_seen()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        var series = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/reports", Ct);
        Assert.Equal(0, series.GetProperty("total").GetInt32());
        Assert.Empty(series.GetProperty("reports").EnumerateArray());

        // Not an error: it is the ordinary state of a machine on which no cron
        // has been set up.
        await Refusals.Problem(
            await admin.GetAsync("/api/machines/ex44/reports/latest", Ct),
            HttpStatusCode.NotFound,
            "not-found");

        var machine = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        Assert.Equal(JsonValueKind.Null, machine.GetProperty("last_seen").ValueKind);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/machines", Ct);
        Assert.Equal(JsonValueKind.Null, list.EnumerateArray().Single().GetProperty("last_seen").ValueKind);
    }

    [Fact]
    public async Task The_series_is_slim_and_the_latest_is_whole()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        using var handed = await machine.PostAsJsonAsync("/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);

        var series = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/reports", Ct);
        Assert.Equal(1, series.GetProperty("total").GetInt32());

        var line = series.GetProperty("reports").EnumerateArray().Single();
        Assert.Equal(1, line.GetProperty("number").GetInt32());
        Assert.Equal(1, line.GetProperty("containers_running").GetInt32());
        Assert.Equal(1, line.GetProperty("containers_total").GetInt32());
        Assert.Equal(60, line.GetProperty("disk_percent").GetInt32());
        Assert.Equal(0.14, line.GetProperty("load1").GetDouble());

        // A line is not a body: the sections are one read away.
        Assert.False(line.TryGetProperty("containers", out _));

        var latest = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/reports/latest", Ct);
        Assert.Equal("ex44", latest.GetProperty("machine").GetString());
        Assert.Equal("0.4.0", latest.GetProperty("agent").GetString());
        Assert.Equal("Ubuntu 26.04 LTS", latest.GetProperty("host").GetProperty("os").GetString());
        Assert.Equal(
            "ghcr.io/datavisionzero/logaffe:1.4.0",
            latest.GetProperty("containers").EnumerateArray().Single().GetProperty("image").GetString());
        Assert.Equal("/", latest.GetProperty("disks").EnumerateArray().Single().GetProperty("mount").GetString());

        var numbered = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/reports/1", Ct);
        Assert.Equal(1, numbered.GetProperty("number").GetInt32());

        await Refusals.Problem(
            await admin.GetAsync("/api/machines/ex44/reports/2", Ct),
            HttpStatusCode.NotFound,
            "not-found");
    }

    [Fact]
    public async Task Last_seen_is_when_the_machine_last_spoke()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        using var handed = await machine.PostAsJsonAsync("/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct);
        var receipt = await handed.Content.ReadFromJsonAsync<JsonElement>(Ct);

        var read = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        Assert.Equal(
            receipt.GetProperty("received_at").GetDateTimeOffset(),
            read.GetProperty("last_seen").GetDateTimeOffset());

        var line = (await admin.GetFromJsonAsync<JsonElement>("/api/machines", Ct)).EnumerateArray().Single();
        Assert.Equal(
            receipt.GetProperty("received_at").GetDateTimeOffset(),
            line.GetProperty("last_seen").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_machine_token_reads_nothing_not_even_its_own_reports()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        using var handed = await machine.PostAsJsonAsync("/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);

        foreach (var address in new[]
        {
            "/api/machines/ex44/reports",
            "/api/machines/ex44/reports/latest",
            "/api/machines/ex44/reports/1",
            "/api/machines/ex44",
        })
        {
            await Refusals.Problem(
                await machine.GetAsync(address, Ct), HttpStatusCode.Unauthorized, "unauthenticated");
        }
    }

    [Fact]
    public async Task The_series_pages_and_refuses_a_limit_that_is_not_a_count()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        // Three reports, each a minute apart in the eyes of the rate limit: the
        // second and the third are put in beside it, the way the sweep test
        // does, because the door takes one a minute.
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        using var handed = await machine.PostAsJsonAsync("/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);

        await instance.AddReportsAsync("ex44", DateTimeOffset.UtcNow.AddHours(-2), 2);

        var first = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/reports?limit=1", Ct);
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(1, first.GetProperty("reports").EnumerateArray().Single().GetProperty("number").GetInt32());

        var second = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44/reports?limit=1&offset=1", Ct);
        Assert.Equal(3, second.GetProperty("reports").EnumerateArray().Single().GetProperty("number").GetInt32());

        await Refusals.Problem(
            await admin.GetAsync("/api/machines/ex44/reports?limit=0", Ct),
            HttpStatusCode.BadRequest,
            "validation");
    }
}
