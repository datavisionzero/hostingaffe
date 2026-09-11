using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The context document (<c>docs/api.md</c>, Machines): everything recorded
/// about one machine, in the order that brings first what is needed first, and
/// small enough that an agent can afford to read it.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MachineContextTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// VISION 16 is the acceptance: well under ten thousand tokens for a machine
    /// with five installations. It is measured rather than estimated, at four
    /// characters to the token, which is the conservative end of what a tokenizer
    /// does with prose and identifiers.
    /// </summary>
    private const int TokenBudget = 10_000;
    private const int CharactersPerToken = 4;

    [Fact]
    public async Task The_document_carries_the_record_in_the_order_that_serves_first_what_is_needed_first()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin, installations: 2);

        var document = await DocumentAsync(admin, "ex44");

        // The machine, with the fields it has and none it has not.
        Assert.StartsWith("# ex44 — The big one\n", document, StringComparison.Ordinal);
        Assert.Contains("A dedicated machine, active.", document, StringComparison.Ordinal);
        Assert.Contains("| provider | hetzner |", document, StringComparison.Ordinal);
        Assert.Contains("| ipv4 | 192.0.2.10 |", document, StringComparison.Ordinal);
        Assert.DoesNotContain("| ipv6 |", document, StringComparison.Ordinal);
        Assert.Contains("The box everything else sits on.", document, StringComparison.Ordinal);

        // Its installations, with the version, the ports and the file list.
        Assert.Contains("### app-1 — Application one", document, StringComparison.Ordinal);
        Assert.Contains("logaffe 1.4.0 · production · application · active", document, StringComparison.Ordinal);
        Assert.Contains("backup: active · monitoring: external · logging: central", document, StringComparison.Ordinal);
        Assert.Contains("path: /opt/compose/app-1\ndata: /srv/services/app-1", document, StringComparison.Ordinal);
        Assert.Contains("ports: 443/tcp:public, 5432/tcp:private", document, StringComparison.Ordinal);
        Assert.Contains("secrets: APP_1_DB_PASSWORD", document, StringComparison.Ordinal);
        Assert.Contains("files: .env.example (revision 1), compose.yml (revision 2)", document, StringComparison.Ordinal);

        // The last deployments, newest first.
        Assert.Contains("Deployments, newest first:\n- 1.4.0 · ", document, StringComparison.Ordinal);
        Assert.Contains("LOG-42", document, StringComparison.Ordinal);

        // The software they are of, and the machine's own files.
        Assert.Contains("## Software\n\n### logaffe — logaffe", document, StringComparison.Ordinal);
        Assert.Contains("image: ghcr.io/datavisionzero/logaffe", document, StringComparison.Ordinal);
        Assert.Contains("## Files of the machine\n\n- sites/app-1.caddy (revision 1)", document, StringComparison.Ordinal);

        // The pages that hang on any of it, and the rules of the instance.
        Assert.Contains("### backup-restore — Restoring a backup", document, StringComparison.Ordinal);
        Assert.Contains("runbook, on machine ex44", document, StringComparison.Ordinal);
        Assert.Contains("### app-1-runbook — Running app-1", document, StringComparison.Ordinal);
        Assert.Contains("runbook, on installation app-1", document, StringComparison.Ordinal);
        Assert.Contains("## Rules of this instance", document, StringComparison.Ordinal);
        Assert.Contains("### tailscale-for-management — Tailscale for management", document, StringComparison.Ordinal);

        // In that order, and nothing before the machine.
        Assert.True(
            document.IndexOf("## Installations", StringComparison.Ordinal)
                < document.IndexOf("## Software", StringComparison.Ordinal)
                && document.IndexOf("## Software", StringComparison.Ordinal)
                    < document.IndexOf("## Files of the machine", StringComparison.Ordinal)
                && document.IndexOf("## Files of the machine", StringComparison.Ordinal)
                    < document.IndexOf("## Pages", StringComparison.Ordinal)
                && document.IndexOf("## Pages", StringComparison.Ordinal)
                    < document.IndexOf("## Rules of this instance", StringComparison.Ordinal),
            $"the sections are out of order:\n{document}");
    }

    /// <summary>
    /// File contents are what would fill a context window, and they are one
    /// read of a file away.
    /// </summary>
    [Fact]
    public async Task The_document_lists_the_files_and_carries_none_of_their_content()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin, installations: 1);

        var document = await DocumentAsync(admin, "ex44");

        Assert.Contains("compose.yml (revision 2)", document, StringComparison.Ordinal);
        Assert.DoesNotContain("mem_limit", document, StringComparison.Ordinal);
        Assert.Contains("File contents are not in this document", document, StringComparison.Ordinal);
    }

    /// <summary>
    /// The measure of VISION 16, on a record of the size it names: five
    /// installations, each with files, deployments and a runbook of its own,
    /// plus the instance's decisions.
    /// </summary>
    [Fact]
    public async Task A_machine_with_five_installations_is_well_under_ten_thousand_tokens()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await AHostAsync(admin, installations: 5);

        var document = await DocumentAsync(admin, "ex44");
        var tokens = document.Length / CharactersPerToken;

        Assert.True(
            tokens < TokenBudget,
            $"the document is {document.Length} characters, about {tokens} tokens, over the budget of {TokenBudget}.");

        // "Well under" is the promise, not "just inside it".
        Assert.True(
            tokens < TokenBudget / 2,
            $"about {tokens} tokens is inside the budget but not well under it:\n{document}");

        TestContext.Current.TestOutputHelper?.WriteLine($"{document.Length} characters, about {tokens} tokens.");

        // And it is not small because it is empty.
        Assert.Contains("### app-5", document, StringComparison.Ordinal);
        Assert.Contains("## Rules of this instance", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_machine_with_nothing_on_it_says_so()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await admin.PostAsJsonAsync("/api/machines", new { key = "cx22", kind = "vps" }, Ct);
        created.EnsureSuccessStatusCode();

        var document = await DocumentAsync(admin, "cx22");

        Assert.Contains("Nothing is installed on this machine.", document, StringComparison.Ordinal);
        Assert.DoesNotContain("## Software", document, StringComparison.Ordinal);
        Assert.DoesNotContain("## Pages", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_that_names_nothing_is_not_found()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);

        await Refusals.Problem(
            await admin.GetAsync("/api/machines/nowhere/context", Ct),
            System.Net.HttpStatusCode.NotFound,
            "not-found");
    }

    private static async Task<string> DocumentAsync(HttpClient client, string key)
    {
        var context = await client.GetFromJsonAsync<JsonElement>($"/api/machines/{key}/context", Ct);
        Assert.Equal(key, context.GetProperty("key").GetString());
        return context.GetProperty("document").GetString()!;
    }

    /// <summary>
    /// A host of the size VISION 16 names: a machine, a software, one file of
    /// the machine per installation, and per installation two files, two
    /// deployments and a runbook — plus three decisions of the instance.
    /// </summary>
    private static async Task AHostAsync(HttpClient client, int installations)
    {
        await Created(client, "/api/machines", new
        {
            key = "ex44",
            name = "The big one",
            kind = "dedicated",
            hostname = "ex44",
            provider = "hetzner",
            plan = "EX44",
            location = "fsn1-dc14",
            os = "Ubuntu 26.04 LTS",
            arch = "amd64",
            cpu = "Intel i5-13500",
            memory = "64G",
            disk = "2×512G NVMe ZFS mirror",
            ipv4 = "192.0.2.10",
            ssh = "ex44",
            measured_at = "2026-09-01T08:00:00Z",
            description = "The box everything else sits on. Two disks, mirrored, and nothing else worth saying here.",
        });

        await Created(client, "/api/software", new
        {
            key = "logaffe",
            name = "logaffe",
            image = "ghcr.io/datavisionzero/logaffe",
            repository = "https://github.com/datavisionzero/logaffe",
            description = "The log everything on this host writes into.",
        });

        await Created(client, "/api/pages", new
        {
            slug = "backup-restore",
            title = "Restoring a backup",
            kind = "runbook",
            attached_to = new { kind = "machine", key = "ex44" },
            body = Paragraphs("Stop the installation, restore the volume, start it again.", 3),
        });

        foreach (var name in new[] { "tailscale-for-management", "one-caddy-per-host", "no-secrets-in-the-record" })
        {
            await Created(client, "/api/pages", new
            {
                slug = name,
                title = Title(name),
                kind = "decision",
                body = Paragraphs($"Why {name.Replace('-', ' ')} is how this instance works.", 2),
            });
        }

        for (var number = 1; number <= installations; number++)
        {
            var key = $"app-{number}";

            await Created(client, "/api/installations", new
            {
                key,
                name = $"Application {Word(number)}",
                machine = "ex44",
                software = "logaffe",
                environment = "production",
                role = "application",
                urls = new[] { $"https://{key}.example.test" },
                ports = new object[]
                {
                    new { port = 443 + number, protocol = "tcp", scope = "public" },
                    new { port = 5432, protocol = "tcp", scope = "private" },
                },
                path = $"/opt/compose/{key}",
                data = $"/srv/services/{key}",
                secrets = new[] { $"{key.ToUpperInvariant().Replace('-', '_')}_DB_PASSWORD" },
                backup = "active",
                monitoring = "external",
                logging = "central",
                description = Paragraphs($"What {key} is for and what it talks to.", 2),
            });

            // The ports of app-1 are the ones the first test reads.
            if (number == 1)
            {
                using var corrected = await client.PatchAsJsonAsync(
                    "/api/installations/app-1",
                    new
                    {
                        ports = new object[]
                        {
                            new { port = 443, protocol = "tcp", scope = "public" },
                            new { port = 5432, protocol = "tcp", scope = "private" },
                        },
                    },
                    Ct);
                corrected.EnsureSuccessStatusCode();
            }

            await Created(client, $"/api/installations/{key}/files", new
            {
                path = "compose.yml",
                content = "services:\n  app:\n    image: ghcr.io/datavisionzero/logaffe:1.3.2\n",
            });
            using var written = await client.PutAsJsonAsync(
                $"/api/installations/{key}/files/compose.yml",
                new { content = "services:\n  app:\n    image: ghcr.io/datavisionzero/logaffe:1.4.0\n    mem_limit: 512m\n" },
                Ct);
            written.EnsureSuccessStatusCode();

            await Created(client, $"/api/installations/{key}/files", new
            {
                path = ".env.example",
                content = "APP_DB_PASSWORD=\n",
            });

            await Created(client, "/api/machines/ex44/files", new
            {
                path = $"sites/{key}.caddy",
                directory = "/etc/caddy",
                content = $"{key}.example.test {{\n  reverse_proxy {key}:8080\n}}\n",
            });

            await Created(client, $"/api/installations/{key}/deployments", new
            {
                version = "1.3.2",
                at = "2026-08-20T09:00:00Z",
                ticket = "LOG-31",
            });

            await Created(client, $"/api/installations/{key}/deployments", new
            {
                version = "1.4.0",
                at = "2026-09-05T12:00:00Z",
                ticket = "LOG-42",
                @ref = "ghcr.io/datavisionzero/logaffe@sha256:0123456789abcdef",
                note = Paragraphs("Rolled forward after the schema migration, with the old volume kept.", 2),
            });

            await Created(client, "/api/pages", new
            {
                slug = $"{key}-runbook",
                title = $"Running {key}",
                kind = "runbook",
                attached_to = new { kind = "installation", key },
                body = Paragraphs($"How {key} is started, stopped and checked.", 4),
            });
        }
    }

    private static async Task Created(HttpClient client, string address, object body)
    {
        using var response = await client.PostAsJsonAsync(address, body, Ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Prose of a believable length, so that the measure measures something.</summary>
    private static string Paragraphs(string opening, int count) =>
        string.Join(
            "\n\n",
            Enumerable.Range(0, count).Select(at => at == 0
                ? opening
                : $"{opening} This is paragraph {at + 1}, and it says the sort of thing a real record says at this length — what was checked, what it depends on, and what to look at when it stops working."));

    private static string Title(string slug) =>
        string.Concat(char.ToUpper(slug[0], CultureInfo.InvariantCulture), slug[1..].Replace('-', ' '));

    private static string Word(int number) => number switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        _ => "five",
    };
}
