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

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("hostingaffe", document["info"]!["title"]!.GetValue<string>());
        Assert.Null(document["servers"]);

        var paths = document["paths"]!.AsObject().Select(path => path.Key).Order(StringComparer.Ordinal);
        Assert.Equal(
            [
                "/admin/projects", "/admin/smtp", "/admin/smtp/test",
                "/agents", "/agents/{id}",
                "/comments/{id}",
                "/email-changes/confirm",
                "/epics", "/epics/{key}", "/epics/{key}/close", "/epics/{key}/reopen", "/epics/{key}/restore",
                "/invitations/accept",
                "/issues", "/issues/{key}", "/issues/{key}/blocked-by/{blockerKey}", "/issues/{key}/claim", "/issues/{key}/close",
                "/issues/{key}/comments", "/issues/{key}/history", "/issues/{key}/labels/{name}", "/issues/{key}/questions",
                "/issues/{key}/release", "/issues/{key}/reopen", "/issues/{key}/restore", "/issues/{key}/review",
                "/me", "/me/email", "/me/metadata", "/me/password", "/password-recovery", "/password-recovery/complete",
                "/projects", "/projects/{key}", "/projects/{key}/labels", "/projects/{key}/labels/{name}",
                "/projects/{key}/labels/{name}/restore", "/projects/{key}/needs-you", "/projects/{key}/next",
                "/projects/{key}/pages", "/projects/{key}/pages/{slug}", "/projects/{key}/pages/{slug}/restore",
                "/projects/{key}/releases", "/projects/{key}/releases/publish", "/projects/{key}/releases/{name}",
                "/projects/{key}/releases/{name}/issues/{issue}", "/projects/{key}/releases/{name}/retract", "/projects/{key}/restore",
                "/projects/{key}/users", "/projects/{key}/users/{id}",
                "/questions", "/questions/{id}", "/questions/{id}/answer",
                "/session", "/session/bootstrap", "/sessions", "/sessions/{id}",
                "/tokens", "/tokens/{id}", "/users", "/users/{id}", "/users/{id}/deactivate",
                "/users/{id}/invitation", "/users/{id}/reactivate", "/version",
            ],
            paths);

        // The shapes are the ones docs/api.md names, spelled that way in the
        // components so that both generated clients see them under those names.
        var schemas = document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key).ToHashSet();
        Assert.Contains("IdentityRef", schemas);
        Assert.Contains("Me", schemas);
        Assert.Contains("NeedsYouPage", schemas);
        Assert.Contains("Release", schemas);
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
            await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

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
