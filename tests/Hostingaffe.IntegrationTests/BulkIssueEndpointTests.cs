using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>Bulk PATCH and DELETE are the single-issue rules inside one transaction.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class BulkIssueEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_bulk_patch_changes_every_issue_and_preserves_the_requested_order()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await ProjectWithIssues(instance);

        using var response = await admin.PatchAsJsonAsync("/issues", new
        {
            keys = new[] { "PLAN-2", "PLAN-1" },
            changes = new { priority = 3, ready = true, labels = new[] { "feature" } },
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(["PLAN-2", "PLAN-1"], body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        Assert.All(body.GetProperty("items").EnumerateArray(), issue =>
        {
            Assert.Equal(3, issue.GetProperty("priority").GetInt32());
            Assert.True(issue.GetProperty("ready").GetBoolean());
            Assert.Equal("feature", Assert.Single(issue.GetProperty("labels").EnumerateArray()).GetProperty("name").GetString());
        });
    }

    [Fact]
    public async Task The_first_patch_refusal_names_its_key_and_rolls_every_change_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await ProjectWithIssues(instance);

        var problem = await ProjectEndpointTests.Problem(
            await admin.PatchAsJsonAsync("/issues", new
            {
                keys = new[] { "PLAN-1", "PLAN-99", "PLAN-2" },
                changes = new { title = "Changed" },
            }, Ct),
            HttpStatusCode.NotFound,
            "not-found");

        Assert.Equal("PLAN-99", problem.GetProperty("key").GetString());
        Assert.Equal("One", (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct)).GetProperty("title").GetString());
        Assert.Equal("Two", (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-2", Ct)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_first_delete_refusal_names_its_key_and_rolls_every_delete_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await ProjectWithIssues(instance);

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/issues")
        {
            Content = JsonContent.Create(new { keys = new[] { "PLAN-1", "PLAN-99", "PLAN-2" } }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "refused-bulk-delete");
        var problem = await ProjectEndpointTests.Problem(
            await admin.SendAsync(request, Ct), HttpStatusCode.NotFound, "not-found");

        Assert.Equal("PLAN-99", problem.GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/issues/PLAN-1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/issues/PLAN-2", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_bulk_delete_deletes_every_issue_and_replays_as_one_write()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await ProjectWithIssues(instance);
        var content = "{\"keys\":[\"PLAN-1\",\"PLAN-2\"]}";

        static HttpRequestMessage Request(string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, "/issues")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "bulk-delete");
            return request;
        }

        using (var first = await admin.SendAsync(Request(content), Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }
        using (var replay = await admin.SendAsync(Request(content), Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        }
        await ProjectEndpointTests.Problem(await admin.GetAsync("/issues/PLAN-1", Ct), HttpStatusCode.NotFound, "deleted");
        await ProjectEndpointTests.Problem(await admin.GetAsync("/issues/PLAN-2", Ct), HttpStatusCode.NotFound, "deleted");
    }

    [Fact]
    public async Task Bulk_requests_require_one_to_one_hundred_distinct_keys()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await ProjectWithIssues(instance);

        await ProjectEndpointTests.Problem(
            await admin.PatchAsJsonAsync("/issues", new { keys = Array.Empty<string>(), changes = new { title = "x" } }, Ct),
            HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(
            await admin.PatchAsJsonAsync("/issues", new { keys = new[] { "PLAN-1", "plan-1" }, changes = new { title = "x" } }, Ct),
            HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(
            await admin.PatchAsJsonAsync("/issues", new { keys = Enumerable.Range(1, 101).Select(i => $"PLAN-{i}").ToArray(), changes = new { title = "x" } }, Ct),
            HttpStatusCode.UnprocessableEntity, "too-many");
    }

    private static async Task<HttpClient> ProjectWithIssues(AnInstance instance)
    {
        var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var project = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        using var issues = await admin.PostAsJsonAsync("/issues", new
        {
            project = "PLAN",
            issues = new[] { new { title = "One" }, new { title = "Two" } },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, issues.StatusCode);
        return admin;
    }
}
