using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Searching (<c>docs/api.md</c>, Searching): "where was that again", asked
/// once over every field, every Markdown body and every file.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SearchEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// The three the ticket names — a port number in an installation, a word in
    /// a page and a word in a file — each in one call, each saying where it was
    /// found.
    /// </summary>
    [Fact]
    public async Task A_port_a_page_and_a_file_are_each_one_call()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin);

        var port = await HitsAsync(admin, "18502");
        var hit = Assert.Single(port);
        Assert.Equal("installation", Field(hit, "kind"));
        Assert.Equal("logaffe-prod", Field(hit, "key"));
        Assert.Equal("ports", Field(hit, "where"));

        var page = await HitsAsync(admin, "tailscale");
        Assert.Equal("page", Field(Assert.Single(page), "kind"));
        Assert.Equal("backup-restore", Field(page[0], "key"));
        Assert.Equal("body", Field(page[0], "where"));

        var file = await HitsAsync(admin, "mem_limit");
        Assert.Equal("file", Field(Assert.Single(file), "kind"));
        Assert.Equal("compose.override.yml", Field(file[0], "key"));
        Assert.Equal("content", Field(file[0], "where"));
        Assert.Equal("logaffe-prod", file[0].GetProperty("owner").GetProperty("key").GetString());
        Assert.Equal("installation", file[0].GetProperty("owner").GetProperty("kind").GetString());
    }

    /// <summary>
    /// One word across the record: the software by its key, the installation by
    /// its key, the deployment by what it deployed, the page by its title.
    /// </summary>
    [Fact]
    public async Task One_word_answers_across_every_kind()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin);

        var hits = await HitsAsync(admin, "logaffe");
        var kinds = hits.Select(hit => Field(hit, "kind")).ToArray();

        Assert.Contains("software", kinds);
        Assert.Contains("installation", kinds);

        // The kinds come in the order the record is read in, not in the order
        // the query happened to find them.
        Assert.Equal(kinds.OrderBy(kind => Array.IndexOf(Order, kind)), kinds);

        var deployments = await HitsAsync(admin, "LOG-42");
        var deployment = Assert.Single(deployments);
        Assert.Equal("deployment", Field(deployment, "kind"));
        Assert.Equal("logaffe-prod", Field(deployment, "key"));
        Assert.Equal("1.4.0", Field(deployment, "name"));
        Assert.Equal(1, deployment.GetProperty("number").GetInt32());
    }

    /// <summary>
    /// Both directories an installation has are searched, and a path is found
    /// whole: what a backup must take is a field, not a sentence in a
    /// description (ADR 0009).
    /// </summary>
    [Fact]
    public async Task Where_an_installations_data_lies_is_searched_like_where_it_lives()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin);

        foreach (var directory in new[] { "/opt/compose/logaffe", "/srv/services/logaffe" })
        {
            var hit = Assert.Single(await HitsAsync(admin, directory));
            Assert.Equal("installation", Field(hit, "kind"));
            Assert.Equal("logaffe-prod", Field(hit, "key"));
            Assert.Equal("fields", Field(hit, "where"));
        }
    }

    /// <summary>
    /// A secret is found by its name and by the file it lies in, and the hit
    /// says which of an installation's places answered (ADR 0011).
    /// </summary>
    [Fact]
    public async Task A_secret_is_searched_by_its_name_and_by_the_file_it_lies_in()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin);

        foreach (var query in new[] { "LOGAFFE_DB_PASSWORD", "/opt/compose/logaffe/.env.runtime" })
        {
            var hit = Assert.Single(await HitsAsync(admin, query));
            Assert.Equal("installation", Field(hit, "kind"));
            Assert.Equal("logaffe-prod", Field(hit, "key"));
            Assert.Equal("secrets", Field(hit, "where"));
        }

        // An installation whose own fields already answered is one hit, not
        // two: the same installation twice is not two answers.
        Assert.Equal(
            "fields",
            Field(Assert.Single(await HitsAsync(admin, "/opt/compose/logaffe")), "where"));
    }

    /// <summary>
    /// A path is one word to Postgres, so a piece of one is looked for as a
    /// fragment — over the fields, and from three characters up (ADR 0012).
    /// </summary>
    [Fact]
    public async Task A_piece_of_a_path_is_found_where_a_word_is_not()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin);

        // The directory an installation's data lies in, typed one segment
        // short. As a word it is nothing the record contains.
        var data = Assert.Single(await HitsAsync(admin, "/srv/services"));
        Assert.Equal("installation", Field(data, "kind"));
        Assert.Equal("logaffe-prod", Field(data, "key"));
        Assert.Equal("fields", Field(data, "where"));

        // The file a secret lies in, by the name of the file alone.
        var secret = Assert.Single(await HitsAsync(admin, ".env.runtime"));
        Assert.Equal("installation", Field(secret, "kind"));
        Assert.Equal("secrets", Field(secret, "where"));

        // Three characters is where a trigram starts, and below it the search
        // is the word search it always was.
        Assert.Equal("file", Field(Assert.Single(await HitsAsync(admin, ".ym")), "kind"));
        Assert.Empty(await HitsAsync(admin, ".y"));
    }

    /// <summary>
    /// The fragment reaches every surface the words reach: what a file says,
    /// what a runbook says to edit, and where a machine's file lies — which is
    /// a column no other surface carries (ADR 0012).
    /// </summary>
    [Fact]
    public async Task A_piece_of_a_path_answers_across_every_surface()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin);

        await Created(admin, "/api/machines/ex44/files", new
        {
            path = "Caddyfile",
            directory = "/etc/caddy",
            content = "import /srv/caddy/sites/*.caddy\n",
        });

        await Created(admin, "/api/pages", new
        {
            slug = "caddy-certificates",
            title = "Renewing a certificate",
            kind = "runbook",
            body = "Edit /srv/caddy/caddy.env, then reload.",
        });

        var hits = await HitsAsync(admin, "/srv/caddy");
        Assert.Equal(["file", "page"], hits.Select(hit => Field(hit, "kind")));
        Assert.Equal("content", Field(hits[0], "where"));
        Assert.Equal("body", Field(hits[1], "where"));

        // Where the file lies is the file's own letters, and the hit says whose
        // file it is — and where, because a bare `Caddyfile` would not say what
        // the search answered "/etc/caddy" with.
        var lies = Assert.Single(await HitsAsync(admin, "/etc/caddy"));
        Assert.Equal("file", Field(lies, "kind"));
        Assert.Equal("Caddyfile", Field(lies, "key"));
        Assert.Equal("/etc/caddy", Field(lies, "directory"));
        Assert.Equal("path", Field(lies, "where"));
        Assert.Equal("machine", lies.GetProperty("owner").GetProperty("kind").GetString());
        Assert.Equal("ex44", lies.GetProperty("owner").GetProperty("key").GetString());

        // An installation's file has no directory of its own: the
        // installation's path said it once for all of them (ADR 0008), and
        // nothing but a machine's file carries the field at all.
        var says = Assert.Single(await HitsAsync(admin, "mem_limit"));
        Assert.Equal("file", Field(says, "kind"));
        Assert.Equal(JsonValueKind.Null, says.GetProperty("directory").ValueKind);

        // A whole path is a word, and a word it stays: the fragment is looked
        // for beside the words and never instead of them.
        Assert.NotEmpty(await HitsAsync(admin, "/opt/compose/logaffe"));
    }

    [Fact]
    public async Task A_deleted_row_is_not_a_hit()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin);

        Assert.NotEmpty(await HitsAsync(admin, "18502"));

        using var deleted = await admin.DeleteAsync("/api/installations/logaffe-prod", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The installation, its files and its deployments went with it, so
        // nothing under it answers either.
        Assert.Empty(await HitsAsync(admin, "18502"));
        Assert.Empty(await HitsAsync(admin, "mem_limit"));
        Assert.Empty(await HitsAsync(admin, "LOG-42"));
    }

    /// <summary>
    /// A file is searched at the revision it is at: what an older one said
    /// stopped being true when the next was written.
    /// </summary>
    [Fact]
    public async Task A_file_answers_for_the_revision_it_is_at()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin);

        Assert.NotEmpty(await HitsAsync(admin, "mem_limit"));

        using var written = await admin.PutAsJsonAsync(
            "/api/installations/logaffe-prod/files/compose.override.yml",
            new { content = "services:\n  logaffe:\n    cpus: 2\n" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);

        Assert.Empty(await HitsAsync(admin, "mem_limit"));
        Assert.NotEmpty(await HitsAsync(admin, "cpus"));
    }

    [Fact]
    public async Task It_is_capped_rather_than_paginated()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        for (var number = 1; number <= 12; number++)
        {
            using var made = await admin.PostAsJsonAsync(
                "/api/machines",
                new { key = $"ex{number}", kind = "dedicated", description = "Rented from hetzner." },
                Ct);
            Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        }

        Assert.Equal(12, (await HitsAsync(admin, "hetzner")).Count);
        Assert.Equal(5, (await HitsAsync(admin, "hetzner", limit: 5)).Count);

        // The cap holds whatever is asked for.
        Assert.Equal(12, (await HitsAsync(admin, "hetzner", limit: 5000)).Count);
    }

    [Fact]
    public async Task A_search_for_nothing_is_refused()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        var problem = await Refusals.Problem(
            await admin.GetAsync("/api/search?q=%20", Ct), HttpStatusCode.BadRequest, "validation");
        Assert.Equal("q", problem.GetProperty("errors").EnumerateObject().Single().Name);

        await Refusals.Problem(
            await admin.GetAsync("/api/search?q=logaffe&limit=0", Ct), HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task A_search_needs_a_token()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var anyone = instance.ClientWith(null);

        using var refused = await anyone.GetAsync("/api/search?q=logaffe", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static readonly string[] Order =
        ["machine", "software", "installation", "deployment", "file", "page"];

    private static string Field(JsonElement hit, string name) => hit.GetProperty(name).GetString()!;

    private static async Task<IReadOnlyList<JsonElement>> HitsAsync(HttpClient client, string q, int? limit = null)
    {
        var address = $"/api/search?q={Uri.EscapeDataString(q)}" + (limit is { } n ? $"&limit={n}" : "");
        var hits = await client.GetFromJsonAsync<JsonElement>(address, Ct);
        return [.. hits.EnumerateArray()];
    }

    /// <summary>One of everything, each carrying a word only it has.</summary>
    private static async Task AHostAsync(HttpClient client)
    {
        await Created(client, "/api/machines", new
        {
            key = "ex44",
            kind = "dedicated",
            provider = "hetzner",
            location = "fsn1-dc14",
            description = "The box everything else sits on.",
        });

        await Created(client, "/api/software", new
        {
            key = "logaffe",
            name = "logaffe",
            image = "ghcr.io/datavisionzero/logaffe",
            description = "The log everything writes into.",
        });

        await Created(client, "/api/installations", new
        {
            key = "logaffe-prod",
            name = "logaffe",
            machine = "ex44",
            software = "logaffe",
            environment = "production",
            role = "application",
            ports = new object[] { new { port = 18502, protocol = "tcp", scope = "internal" } },
            path = "/opt/compose/logaffe",
            data = "/srv/services/logaffe",
            secrets = new object[]
            {
                new { name = "LOGAFFE_DB_PASSWORD", path = "/opt/compose/logaffe/.env.runtime" },
            },
        });

        await Created(client, "/api/installations/logaffe-prod/files", new
        {
            path = "compose.override.yml",
            content = "services:\n  logaffe:\n    mem_limit: 512m\n",
        });

        await Created(client, "/api/installations/logaffe-prod/deployments", new
        {
            version = "1.4.0",
            ticket = "LOG-42",
            note = "Rolled forward.",
        });

        await Created(client, "/api/pages", new
        {
            slug = "backup-restore",
            title = "Restoring a backup",
            kind = "runbook",
            body = "Reach the host over tailscale, stop it, restore the volume.",
        });
    }

    private static async Task Created(HttpClient client, string address, object body)
    {
        using var response = await client.PostAsJsonAsync(address, body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
