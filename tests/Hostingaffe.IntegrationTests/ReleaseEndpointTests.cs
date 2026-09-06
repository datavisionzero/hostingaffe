using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class ReleaseEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Done_collects_publish_freezes_and_reopen_records_shipping_again()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);

        var initial = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/releases", Ct);
        var open = Assert.Single(initial.EnumerateArray());
        Assert.Equal("unreleased", open.GetProperty("name").GetString());
        Assert.Equal(0, open.GetProperty("issues").GetInt32());

        await CreateAsync(admin, "Delivered");
        await CreateAsync(admin, "Canceled");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done", result = "Built." }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/issues/PLAN-2/close", new { status = "canceled", result = "Not built." }, Ct)).StatusCode);

        var unreleased = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/releases/unreleased", Ct);
        Assert.Equal("PLAN-1", Assert.Single(unreleased.GetProperty("issues").EnumerateArray()).GetProperty("key").GetString());
        Assert.Equal("unreleased", (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct)).GetProperty("release").GetString());
        Assert.Equal(JsonValueKind.Null, (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-2", Ct)).GetProperty("release").ValueKind);

        using var publishedResponse = await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "v1.0.0", description = "First." }, Ct);
        Assert.Equal(HttpStatusCode.Created, publishedResponse.StatusCode);
        var published = await publishedResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("v1.0.0", published.GetProperty("name").GetString());
        Assert.Equal("maintainer", published.GetProperty("published_by").GetProperty("name").GetString());
        Assert.NotEqual(JsonValueKind.Null, published.GetProperty("published_at").ValueKind);

        using var duplicate = await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "V1.0.0" }, Ct);
        await ProjectEndpointTests.Problem(duplicate, HttpStatusCode.Conflict, "release-exists");

        await admin.PostAsJsonAsync("/issues/PLAN-1/reopen", new { }, Ct);
        Assert.Equal("v1.0.0", (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct)).GetProperty("release").GetString());
        await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done", result = "Fixed." }, Ct);
        var releases = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/releases", Ct);
        Assert.Equal(["unreleased", "v1.0.0"], releases.EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        Assert.All(releases.EnumerateArray(), r => Assert.Equal(1, r.GetProperty("issues").GetInt32()));

        using var deletion = await admin.DeleteAsync("/issues/PLAN-1", Ct);
        await ProjectEndpointTests.Problem(deletion, HttpStatusCode.UnprocessableEntity, "in-published-release");
    }

    [Fact]
    public async Task Sub_issues_ship_with_the_parent_and_notes_can_be_annotated()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await CreateAsync(admin, "Parent");
        using var child = await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new[] { new { title = "Child", parent = "PLAN-1" } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, child.StatusCode);

        await admin.PostAsJsonAsync("/issues/PLAN-2/close", new { status = "done", result = "Part." }, Ct);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/releases/unreleased", Ct)).GetProperty("issues").EnumerateArray());
        await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done", result = "Whole." }, Ct);

        var release = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/releases/unreleased", Ct);
        Assert.Equal(["PLAN-1", "PLAN-2"], release.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        using var changed = await admin.PatchAsJsonAsync("/projects/PLAN/releases/unreleased", new { description = "Draft notes." }, Ct);
        Assert.Equal("Draft notes.", (await changed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("description").GetString());
    }

    // VISION 7 says it in as many words: "Moving a ticket by hand still works —
    // a ticket that has not shipped yet simply does not belong."
    [Fact]
    public async Task An_issue_is_put_into_the_open_release_and_taken_out_of_it_by_hand()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await CreateAsync(admin, "Delivered");
        await CreateAsync(admin, "Not delivered");
        await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done", result = "Built." }, Ct);

        using var taken = await admin.DeleteAsync("/projects/PLAN/releases/unreleased/issues/PLAN-1", Ct);
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        Assert.Empty((await taken.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issues").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct)).GetProperty("release").ValueKind);

        using var put = await admin.PutAsync("/projects/PLAN/releases/unreleased/issues/PLAN-2", null, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("PLAN-2", Assert.Single((await put.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issues").EnumerateArray()).GetProperty("key").GetString());
        Assert.Equal("unreleased", (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-2", Ct)).GetProperty("release").GetString());

        var history = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-2/history", Ct);
        Assert.Contains(history.EnumerateArray(), e => e.GetProperty("field").GetString() == "release" && e.GetProperty("new_value").GetString() == "unreleased");

        // A published release is a record: neither act touches it.
        await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "v1.0.0" }, Ct);
        using var intoPublished = await admin.PutAsync("/projects/PLAN/releases/v1.0.0/issues/PLAN-1", null, Ct);
        await ProjectEndpointTests.Problem(intoPublished, HttpStatusCode.UnprocessableEntity, "in-published-release");
        using var outOfPublished = await admin.DeleteAsync("/projects/PLAN/releases/v1.0.0/issues/PLAN-2", Ct);
        await ProjectEndpointTests.Problem(outOfPublished, HttpStatusCode.UnprocessableEntity, "in-published-release");

        // And what shipped cannot be put into the open one on top of that.
        using var again = await admin.PutAsync("/projects/PLAN/releases/unreleased/issues/PLAN-2", null, Ct);
        await ProjectEndpointTests.Problem(again, HttpStatusCode.UnprocessableEntity, "in-published-release");
    }

    [Fact]
    public async Task The_newest_publication_is_renamed_and_an_older_one_is_not()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "v1.0.0", description = "First." }, Ct);

        using var renamed = await admin.PatchAsJsonAsync("/projects/PLAN/releases/v1.0.0", new { name = "v1.0.1" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var fixedUp = await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("v1.0.1", fixedUp.GetProperty("name").GetString());
        // A field left out is left alone; the notes survive the rename.
        Assert.Equal("First.", fixedUp.GetProperty("description").GetString());

        await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "v2.0.0" }, Ct);
        using var late = await admin.PatchAsJsonAsync("/projects/PLAN/releases/v1.0.1", new { name = "v1.0.2" }, Ct);
        await ProjectEndpointTests.Problem(late, HttpStatusCode.UnprocessableEntity, "transition");

        using var taken = await admin.PatchAsJsonAsync("/projects/PLAN/releases/v2.0.0", new { name = "V1.0.1" }, Ct);
        await ProjectEndpointTests.Problem(taken, HttpStatusCode.Conflict, "release-exists");

        using var open = await admin.PatchAsJsonAsync("/projects/PLAN/releases/unreleased", new { name = "v3" }, Ct);
        await ProjectEndpointTests.Problem(open, HttpStatusCode.UnprocessableEntity, "transition");
    }

    [Fact]
    public async Task A_publication_is_taken_back_while_nothing_has_followed_it()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await CreateAsync(admin, "Delivered");
        await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done", result = "Built." }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "v1.0.0", description = "Oops." }, Ct);

        using var retracted = await admin.PostAsync("/projects/PLAN/releases/v1.0.0/retract", null, Ct);
        Assert.Equal(HttpStatusCode.OK, retracted.StatusCode);
        var open = await retracted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("unreleased", open.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, open.GetProperty("published_at").ValueKind);
        // The tickets stay where they are, and the empty open release goes.
        Assert.Equal("PLAN-1", Assert.Single(open.GetProperty("issues").EnumerateArray()).GetProperty("key").GetString());
        var releases = await admin.GetFromJsonAsync<JsonElement>("/projects/PLAN/releases", Ct);
        Assert.Equal(["unreleased"], releases.EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        Assert.Equal("Oops.", Assert.Single(releases.EnumerateArray()).GetProperty("description").GetString());

        await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "v1.0.0" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "v2.0.0" }, Ct);
        using var followed = await admin.PostAsync("/projects/PLAN/releases/v1.0.0/retract", null, Ct);
        await ProjectEndpointTests.Problem(followed, HttpStatusCode.UnprocessableEntity, "transition");

        using var never = await admin.PostAsync("/projects/PLAN/releases/unreleased/retract", null, Ct);
        await ProjectEndpointTests.Problem(never, HttpStatusCode.UnprocessableEntity, "transition");
    }

    // A fumble is corrected at once. Once work has closed into the release that
    // the publication opened, taking it back is no longer a correction.
    [Fact]
    public async Task A_publication_that_work_has_closed_on_top_of_stays()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = instance.ClientWith(AnInstance.BootstrapToken);
        await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        await admin.PostAsJsonAsync("/projects/PLAN/releases/publish", new { name = "v1.0.0" }, Ct);
        await CreateAsync(admin, "Since then");
        await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done", result = "Built." }, Ct);

        using var refused = await admin.PostAsync("/projects/PLAN/releases/v1.0.0/retract", null, Ct);
        await ProjectEndpointTests.Problem(refused, HttpStatusCode.UnprocessableEntity, "transition");
    }

    private static async Task CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new[] { new { title } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
