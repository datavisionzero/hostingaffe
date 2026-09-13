using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Reports are swept after thirty days (<c>docs/operations.md</c>,
/// <c>HOSTINGAFFE_REPORT_RETENTION_DAYS</c>): nothing about a three-month-old
/// sample is worth keeping for ever, and the sweep rides along with the purge
/// rather than being a second moving part in the operation.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReportSweepTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_is_past_the_window_goes_and_the_latest_stays()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");

        // Two on either side of the window, and one from long before it that is
        // the machine's latest — so that "the latest survives" is tested by a
        // row the window would otherwise take.
        await instance.AddReportsAsync("ex44", DateTimeOffset.UtcNow.AddDays(-90), 2);
        await instance.AddReportsAsync("ex44", DateTimeOffset.UtcNow.AddDays(-40), 2);
        await instance.AddReportsAsync("ex44", DateTimeOffset.UtcNow.AddDays(-2), 2);

        Assert.Equal(6, await CountAsync(instance));

        // A write of any kind carries the purge, and so does the arrival of a
        // report — which is what makes the sweep keep up with a cron.
        using var machine = instance.ClientWith(await instance.AddMachineTokenAsync("ex44"));
        using var handed = await machine.PostAsJsonAsync(
            "/api/machines/ex44/reports", ReportEndpointTests.AReport(), Ct);
        Assert.Equal(HttpStatusCode.Created, handed.StatusCode);

        // The four past the window went; the two inside it and the one that
        // just arrived stayed.
        Assert.Equal(3, await CountAsync(instance));
    }

    /// <summary>
    /// A machine that has been silent for six weeks keeps the one thing worth
    /// knowing about it: when it last spoke, and how it was doing then.
    /// </summary>
    [Fact]
    public async Task A_silent_machine_keeps_its_last_word()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");
        await ReportEndpointTests.Machine(admin, "cx22");

        await instance.AddReportsAsync("ex44", DateTimeOffset.UtcNow.AddDays(-90), 1);

        // Somebody else's write carries the purge too.
        using var written = await admin.PostAsJsonAsync("/api/machines", new { key = "another", kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.Created, written.StatusCode);

        Assert.Equal(1, await CountAsync(instance));

        var latest = await admin.GetAsync("/api/machines/ex44/reports/latest", Ct);
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        latest.Dispose();
    }

    /// <summary>Zero keeps every report, for whoever wants that.</summary>
    [Fact]
    public async Task A_retention_of_zero_sweeps_nothing()
    {
        await using var instance = await AnInstance.ConfiguredAsync(
            postgres,
            new Dictionary<string, string?> { [InstanceSettings.ReportRetentionVariable] = "0" });

        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await ReportEndpointTests.Machine(admin, "ex44");
        await instance.AddReportsAsync("ex44", DateTimeOffset.UtcNow.AddDays(-400), 3);

        using var written = await admin.PostAsJsonAsync("/api/machines", new { key = "another", kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.Created, written.StatusCode);

        Assert.Equal(3, await CountAsync(instance));
    }

    /// <summary>
    /// The default takes effect without anyone touching a file: an instance
    /// that runs today runs after `docker compose pull && up -d` (CLAUDE.md).
    /// </summary>
    [Fact]
    public void The_default_is_thirty_days_and_needs_no_variable()
    {
        var settings = InstanceSettings.FromVariables(null, null);

        Assert.Equal(TimeSpan.FromDays(30), settings.ReportRetention);
        Assert.True(settings.SweepsReports);
        Assert.False(InstanceSettings.FromVariables(null, "0").SweepsReports);
        Assert.Throws<ArgumentException>(() => InstanceSettings.FromVariables(null, "a fortnight"));
    }

    private static async Task<int> CountAsync(AnInstance instance)
    {
        await using var context = Migrated.ContextFor(instance.ConnectionString);
        return await context.Reports.CountAsync(Ct);
    }
}
