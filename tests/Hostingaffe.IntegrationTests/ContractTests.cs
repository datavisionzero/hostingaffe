using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The contract as the running instance serves it, against the one checked in
/// (ADR 0005). CI makes the same comparison against a real Postgres; this makes
/// it before the push, so that a changed shape is a red test on the desk and
/// not a red trunk.
/// </summary>
/// <remarks>
/// The comparison is structural, not textual: what the two documents say has to
/// agree, not how they were formatted. Regenerating is the same test with
/// <c>HOSTINGAFFE_CAPTURE_CONTRACT=1</c>, which writes the served document over
/// the checked-in one — formatted the way CI's capture step formats it, so that
/// the two never differ by whitespace — and then passes.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ContractTests(PostgresFixture postgres)
{
    private const string CaptureVariable = "HOSTINGAFFE_CAPTURE_CONTRACT";

    [Fact]
    public async Task The_document_is_served_without_a_token_and_names_every_endpoint()
    {
        await using var instance = await AnInstance.StartedAsync(postgres, null, null);
        using var client = instance.ClientWith(null);

        using var response = await client.GetAsync("/api/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("hostingaffe", document["info"]!["title"]!.GetValue<string>());
        Assert.Null(document["servers"]);

        var paths = document["paths"]!.AsObject().Select(path => path.Key).Order(StringComparer.Ordinal);
        Assert.Equal(
            [
                "/api/admin/smtp", "/api/admin/smtp/test",
                "/api/agents", "/api/agents/{id}",
                "/api/email-changes/confirm",
                "/api/invitations/accept",
                "/api/me", "/api/me/email", "/api/me/metadata", "/api/me/password",
                "/api/pages", "/api/pages/{slug}", "/api/pages/{slug}/history", "/api/pages/{slug}/restore",
                "/api/password-recovery", "/api/password-recovery/complete",
                "/api/session", "/api/session/bootstrap", "/api/sessions", "/api/sessions/{id}",
                "/api/tokens", "/api/tokens/{id}", "/api/users", "/api/users/{id}", "/api/users/{id}/deactivate",
                "/api/users/{id}/invitation", "/api/users/{id}/reactivate", "/api/version",
            ],
            paths);

        // The shapes are the ones docs/api.md names, spelled that way in the
        // components so that both generated clients see them under those names.
        var schemas = document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key).ToHashSet();
        Assert.Contains("IdentityRef", schemas);
        Assert.Contains("Me", schemas);
        Assert.Contains("Page", schemas);
        Assert.Contains("PageSummary", schemas);
        Assert.Contains("HistoryEntry", schemas);
        Assert.Contains("SmtpStatus", schemas);
        Assert.Contains("VersionResponse", schemas);
        Assert.Contains("ProblemDetails", schemas);
    }

    [Fact]
    public async Task The_served_document_is_the_checked_in_one()
    {
        await using var instance = await AnInstance.StartedAsync(postgres, null, null);
        using var client = instance.ClientWith(null);

        var served = JsonNode.Parse(
            await client.GetStringAsync("/api/openapi/v1.json", TestContext.Current.CancellationToken))!;

        var path = Path.Combine(RepositoryRoot(), "docs", "api", "openapi.json");

        if (Environment.GetEnvironmentVariable(CaptureVariable) is "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, Formatted(served), TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(path), $"{path} is missing; capture it with {CaptureVariable}=1.");

        var checkedIn = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        Assert.True(
            JsonNode.DeepEquals(served, checkedIn),
            $"The instance serves a document other than docs/api/openapi.json. "
            + $"Regenerate it with {CaptureVariable}=1 and commit it with the change (ADR 0005).");
    }

    /// <summary>
    /// Two-space indent, one member per line, nothing escaped that need not be,
    /// a newline at the end — the same bytes CI's Python formatting produces
    /// for the same document, so that a local capture and CI's never disagree.
    /// </summary>
    private static string Formatted(JsonNode document) =>
        document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Hostingaffe.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No Hostingaffe.slnx above the test binary.");
    }
}
