using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The history read across every subject (<c>docs/api.md</c>, The history):
/// what happened lately, with the deployments mixed in.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class HistoryReadTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string Address = "/api/history";

    /// <summary>
    /// The two things this reading is for: an act that touched three fields is
    /// one event carrying three changes, and a deployment is an event of its
    /// own whose change is the version it went to.
    /// </summary>
    [Fact]
    public async Task An_act_is_one_event_and_a_deployment_is_one_too()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);
        using var provider = await admin.PostAsJsonAsync("/api/providers", new { key = "hetzner" }, Ct);
        Assert.Equal(HttpStatusCode.Created, provider.StatusCode);

        var machine = await admin.GetFromJsonAsync<JsonElement>("/api/machines/ex44", Ct);
        using var changed = await Patch(
            admin,
            "/api/machines/ex44?note=the%20new%20box",
            machine.GetProperty("updated_at").GetString()!,
            new { provider = "hetzner", os = "Debian 12", arch = "arm64" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        using var deployed = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/deployments", new { version = "0.5.0" }, Ct);
        Assert.Equal(HttpStatusCode.Created, deployed.StatusCode);

        var events = await EventsAsync(admin, Address);

        // Newest first: the deployment, then the three fields as one act, then
        // the births the ground laid.
        var deployment = events[0];
        Assert.Equal("deployment", Field(deployment, "subject_kind"));
        Assert.Equal("logaffe-prod", Field(deployment, "subject"));
        Assert.Equal("ex44", Field(deployment, "machine"));
        Assert.Equal(2, deployment.GetProperty("number").GetInt32());
        var version = Assert.Single(deployment.GetProperty("changes").EnumerateArray());
        Assert.Equal("version", Field(version, "field"));
        Assert.Equal("0.4.0", Field(version, "old_value"));
        Assert.Equal("0.5.0", Field(version, "new_value"));

        var act = events[1];
        Assert.Equal("machine", Field(act, "subject_kind"));
        Assert.Equal("ex44", Field(act, "subject"));
        Assert.Equal("ex44", Field(act, "machine"));
        Assert.Equal("the new box", Field(act, "note"));
        Assert.Equal(
            ["arch", "os", "provider"],
            act.GetProperty("changes").EnumerateArray().Select(one => Field(one, "field")).Order());
        Assert.Equal("maintainer", act.GetProperty("actor").GetProperty("name").GetString());

        // The installation was created with a version, so its birth and the
        // deployment that came with it are two events sharing one moment — and
        // a deployment is in this reading once, as the record it is, never
        // again as the history row its recording wrote beside it.
        Assert.Equal(
            ["deployment", "machine", "provider", "installation", "deployment", "software", "machine"],
            events.Select(one => Field(one, "subject_kind")));
    }

    /// <summary>
    /// The filter a machine screen asks with: everything that hangs on one
    /// machine — the machine, its installations, their deployments, the files
    /// of both and the pages attached to them — and nothing of another.
    /// </summary>
    [Fact]
    public async Task A_machine_carries_what_hangs_on_it()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var second = await admin.PostAsJsonAsync("/api/machines", new { key = "ex52", kind = "vps" }, Ct);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using var file = await admin.PostAsJsonAsync(
            "/api/installations/logaffe-prod/files",
            new { path = "compose.override.yml", content = "services: {}" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, file.StatusCode);

        using var page = await admin.PostAsJsonAsync(
            "/api/pages",
            new { slug = "backup-restore", title = "Backup", attached_to = new { kind = "machine", key = "ex44" } },
            Ct);
        Assert.Equal(HttpStatusCode.Created, page.StatusCode);

        var mine = await EventsAsync(admin, $"{Address}?machine=ex44");

        Assert.All(mine, one => Assert.Equal("ex44", Field(one, "machine")));

        // A file and a page say what they hang on, the way a search hit does:
        // a path is an address only under its owner.
        var written = mine.Single(one => Field(one, "subject_kind") == "file");
        Assert.Equal("installation", written.GetProperty("owner").GetProperty("kind").GetString());
        Assert.Equal("logaffe-prod", written.GetProperty("owner").GetProperty("key").GetString());

        var written_down = mine.Single(one => Field(one, "subject_kind") == "page");
        Assert.Equal("machine", written_down.GetProperty("owner").GetProperty("kind").GetString());
        Assert.Equal("ex44", written_down.GetProperty("owner").GetProperty("key").GetString());
        Assert.Equal(
            ["page", "file", "installation", "deployment", "machine"],
            mine.Select(one => Field(one, "subject_kind")));

        // The software belongs to no machine, so it is in the instance's
        // reading and in neither machine's.
        var all = await EventsAsync(admin, Address);
        Assert.Contains(all, one => Field(one, "subject_kind") == "software");
        Assert.DoesNotContain(mine, one => Field(one, "subject_kind") == "software");

        var other = await EventsAsync(admin, $"{Address}?machine=ex52");
        var born = Assert.Single(other);
        Assert.Equal("machine", Field(born, "subject_kind"));
        Assert.Equal("ex52", Field(born, "subject"));
    }

    /// <summary>
    /// The walk: a page at a time reads exactly what the whole reading holds,
    /// in the same order, with nothing seen twice and nothing skipped — and the
    /// events here share their moment, because the ground was laid in one call
    /// after another.
    /// </summary>
    [Fact]
    public async Task The_cursor_walks_without_a_gap_or_a_double()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        // Two deployments backfilled onto the same moment: the same `at` on two
        // events is what a cursor of time alone would fall over.
        await Deploy(admin, "1.0.0", at: "2026-02-01T10:00:00Z");
        await Deploy(admin, "1.1.0", at: "2026-02-01T10:00:00Z");

        var whole = await EventsAsync(admin, $"{Address}?limit=200");
        Assert.True(whole.Count >= 6);

        var walked = new List<JsonElement>();
        string? before = null;

        while (true)
        {
            var page = await EventsAsync(
                admin, before is null ? $"{Address}?limit=1" : $"{Address}?limit=1&before={before}");

            if (page.Count == 0)
            {
                break;
            }

            walked.AddRange(page);
            before = Field(page[^1], "cursor");
        }

        Assert.Equal(
            whole.Select(Line),
            walked.Select(Line));
    }

    /// <summary>
    /// A deleted subject keeps its events, and the last of them says what
    /// became of it. The history survives the deletion of what it describes
    /// (VISION 7), and a reading of what happened that dropped it would answer
    /// the opposite of what it was asked.
    /// </summary>
    [Fact]
    public async Task A_deletion_is_the_last_thing_that_happened()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        using var deleted = await admin.DeleteAsync("/api/installations/logaffe-prod?note=never%20ran", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var events = await EventsAsync(admin, $"{Address}?machine=ex44");
        var last = events[0];

        Assert.Equal("installation", Field(last, "subject_kind"));
        Assert.Equal("logaffe-prod", Field(last, "subject"));
        Assert.Equal("never ran", Field(last, "note"));
        Assert.Equal("deleted", Field(Assert.Single(last.GetProperty("changes").EnumerateArray()), "field"));
    }

    /// <summary>
    /// The three ways to ask for something that is not there: a kind outside
    /// the set, a machine that does not exist, and a cursor nobody handed out.
    /// </summary>
    [Fact]
    public async Task What_is_not_a_kind_a_machine_or_a_cursor_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await Ground(admin);

        await Refusals.Problem(
            await admin.GetAsync($"{Address}?kind=report", Ct), HttpStatusCode.BadRequest, "validation");

        await Refusals.Problem(
            await admin.GetAsync($"{Address}?machine=ex99", Ct), HttpStatusCode.NotFound, "not-found");

        await Refusals.Problem(
            await admin.GetAsync($"{Address}?before=not-a-cursor", Ct), HttpStatusCode.BadRequest, "cursor-invalid");

        // The kinds that are kinds answer, and the deployment kind carries both
        // the records and the corrections made to them.
        var deployments = await EventsAsync(admin, $"{Address}?kind=deployment");
        Assert.All(deployments, one => Assert.Equal("deployment", Field(one, "subject_kind")));
        Assert.Single(deployments);
    }

    /// <summary>A machine, a software, an installation with a version, and the deployment that came with it.</summary>
    private static async Task Ground(HttpClient client)
    {
        using var machine = await client.PostAsJsonAsync("/api/machines", new { key = "ex44", kind = "dedicated" }, Ct);
        Assert.Equal(HttpStatusCode.Created, machine.StatusCode);

        using var software = await client.PostAsJsonAsync("/api/software", new { key = "logaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, software.StatusCode);

        using var installation = await client.PostAsJsonAsync(
            "/api/installations",
            new
            {
                key = "logaffe-prod",
                machine = "ex44",
                software = "logaffe",
                environment = "production",
                role = "application",
                version = "0.4.0",
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, installation.StatusCode);
    }

    private static async Task Deploy(HttpClient client, string version, string? at = null)
    {
        using var recorded = await client.PostAsJsonAsync(
            "/api/installations/logaffe-prod/deployments", new { version, at }, Ct);
        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
    }

    private static async Task<HttpResponseMessage> Patch(
        HttpClient client, string address, string version, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, address)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request, Ct);
    }

    private static async Task<IReadOnlyList<JsonElement>> EventsAsync(HttpClient client, string address) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(address, Ct)).EnumerateArray()];

    /// <summary>An event as a line, so that two readings can be compared by what they say.</summary>
    private static string Line(JsonElement one) =>
        $"{Field(one, "at")} {Field(one, "subject_kind")} {Field(one, "subject")} {Field(one, "cursor")}";

    private static string? Field(JsonElement one, string member) =>
        one.GetProperty(member).ValueKind is JsonValueKind.Null ? null : one.GetProperty(member).GetString();
}
