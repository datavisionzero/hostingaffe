using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hostingaffe.IntegrationTests;

/// <summary>Comments, questions and the history over HTTP (VISION 7, <c>docs/api.md</c>).</summary>
[Collection(nameof(PostgresCollection))]
public sealed class ConversationEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_open_question_takes_the_issue_out_of_next_and_the_answer_brings_it_back()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "A" });
        using var agent = await Agent(instance, admin, "one");

        Assert.Equal(["PLAN-1"], await NextKeys(agent));

        using var asked = await agent.PostAsJsonAsync("/issues/PLAN-1/questions", new { question = "Which Postgres?" }, Ct);
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        var question = await asked.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("one", question.GetProperty("asked_by").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, question.GetProperty("answer").ValueKind);

        Assert.Empty(await NextKeys(agent));
        var preview = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(1, preview.GetProperty("reasons").GetProperty("waiting_for_answer").GetInt32());

        var read = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct);
        Assert.Equal(1, read.GetProperty("open_questions").GetInt32());

        var id = question.GetProperty("id").GetGuid();
        using var answered = await admin.PostAsJsonAsync($"/questions/{id}/answer", new { answer = "18." }, Ct);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var pair = await answered.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("18.", pair.GetProperty("answer").GetString());
        Assert.Equal("maintainer", pair.GetProperty("answered_by").GetProperty("name").GetString());

        Assert.Equal(["PLAN-1"], await NextKeys(agent));

        // A second answer, an answer to nothing, a question on a closed issue.
        await ProjectEndpointTests.Problem(await admin.PostAsJsonAsync($"/questions/{id}/answer", new { answer = "17." }, Ct), HttpStatusCode.UnprocessableEntity, "transition");
        await ProjectEndpointTests.Problem(await admin.PostAsJsonAsync($"/questions/{Guid.NewGuid()}/answer", new { answer = "?" }, Ct), HttpStatusCode.NotFound, "not-found");
        await admin.PostAsJsonAsync("/issues/PLAN-1/close", new { status = "done" }, Ct);
        await ProjectEndpointTests.Problem(await agent.PostAsJsonAsync("/issues/PLAN-1/questions", new { question = "Still?" }, Ct), HttpStatusCode.UnprocessableEntity, "transition");
        await ProjectEndpointTests.Problem(await agent.PostAsJsonAsync("/issues/PLAN-1/comments", new { body = "  " }, Ct), HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Asking_does_not_release_the_claim_and_the_holders_words_extend_it_while_a_strangers_do_not()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "A" });
        using var agent = await Agent(instance, admin, "one");
        await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);

        await using var context = Migrated.ContextFor(instance.ConnectionString);
        async Task<DateTimeOffset?> Expiry()
        {
            context.ChangeTracker.Clear();
            return (await context.Issues.AsNoTracking().SingleAsync(Ct)).Claim?.ExpiresAt;
        }

        async Task Age() => await context.Database.ExecuteSqlRawAsync(
            "update issue set claim_extended_at = claim_extended_at - interval '1 hour', claim_expires_at = claim_expires_at - interval '1 hour'", Ct);

        // The asker holds the claim: asking keeps it and extends it.
        await Age();
        var before = await Expiry();
        using var asked = await agent.PostAsJsonAsync("/issues/PLAN-1/questions", new { question = "How?" }, Ct);
        var issue = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct);
        Assert.Equal("in_progress", issue.GetProperty("status").GetString());
        Assert.Equal("one", issue.GetProperty("claim").GetProperty("holder").GetProperty("name").GetString());
        Assert.True(await Expiry() > before);

        // A stranger's comment moves updated_at and leaves the claim alone.
        await Age();
        before = await Expiry();
        var updatedBefore = issue.GetProperty("updated_at").GetString();
        using var commented = await admin.PostAsJsonAsync("/issues/PLAN-1/comments", new { body = "How far did you get?" }, Ct);
        Assert.Equal(HttpStatusCode.Created, commented.StatusCode);
        Assert.Equal(before, await Expiry());
        Assert.NotEqual(updatedBefore, (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct)).GetProperty("updated_at").GetString());

        // The holder's comment, and the holder's answer, extend.
        await Age();
        before = await Expiry();
        await agent.PostAsJsonAsync("/issues/PLAN-1/comments", new { body = "Halfway." }, Ct);
        Assert.True(await Expiry() > before);

        await Age();
        before = await Expiry();
        var id = (await asked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await admin.PostAsJsonAsync($"/questions/{id}/answer", new { answer = "Like so." }, Ct);
        Assert.Equal(before, await Expiry());
    }

    [Fact]
    public async Task Questions_list_across_the_project_open_by_default_and_page()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "A" }, new { title = "B" });
        using var agent = await Agent(instance, admin, "one");

        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            using var asked = await agent.PostAsJsonAsync($"/issues/PLAN-{1 + i % 2}/questions", new { question = $"Q{i}" }, Ct);
            ids.Add((await asked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());
        }

        await admin.PostAsJsonAsync($"/questions/{ids[0]}/answer", new { answer = "A0" }, Ct);

        var open = await admin.GetFromJsonAsync<JsonElement>("/questions?project=PLAN", Ct);
        Assert.Equal(4, open.GetProperty("total").GetInt32());
        Assert.Equal(["Q1", "Q2", "Q3", "Q4"], open.GetProperty("items").EnumerateArray().Select(q => q.GetProperty("question").GetString()));
        Assert.Equal("PLAN-2", open.GetProperty("items")[0].GetProperty("issue").GetProperty("key").GetString());
        Assert.Equal("B", open.GetProperty("items")[0].GetProperty("issue").GetProperty("title").GetString());

        var answered = await admin.GetFromJsonAsync<JsonElement>("/questions?project=PLAN&open=false", Ct);
        Assert.Equal(["Q0"], answered.GetProperty("items").EnumerateArray().Select(q => q.GetProperty("question").GetString()));

        var ofOne = await admin.GetFromJsonAsync<JsonElement>("/questions?issue=PLAN-1", Ct);
        Assert.Equal(["Q2", "Q4"], ofOne.GetProperty("items").EnumerateArray().Select(q => q.GetProperty("question").GetString()));

        var first = await admin.GetFromJsonAsync<JsonElement>("/questions?project=PLAN&limit=3", Ct);
        Assert.True(first.GetProperty("has_more").GetBoolean());
        var second = await admin.GetFromJsonAsync<JsonElement>($"/questions?project=PLAN&limit=3&cursor={Uri.EscapeDataString(first.GetProperty("next_cursor").GetString()!)}", Ct);
        Assert.Equal(["Q4"], second.GetProperty("items").EnumerateArray().Select(q => q.GetProperty("question").GetString()));
        Assert.False(second.GetProperty("has_more").GetBoolean());
        await ProjectEndpointTests.Problem(await admin.GetAsync($"/questions?project=PLAN&open=false&cursor={Uri.EscapeDataString(first.GetProperty("next_cursor").GetString()!)}", Ct), HttpStatusCode.BadRequest, "cursor-invalid");
    }

    [Fact]
    public async Task Full_text_search_finds_issue_text_comments_questions_and_answers_without_changing_order()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin,
            new { title = "Parser", description = "Handles claim-expired identifiers" },
            new { title = "Database", description = "Uses a transaction for update" },
            new { title = "Unrelated", description = "Nothing useful here" });

        await admin.PostAsJsonAsync("/issues/PLAN-1/comments", new { body = "A flaky regression" }, Ct);
        using var asked = await admin.PostAsJsonAsync("/issues/PLAN-2/questions", new { question = "Which isolation level?" }, Ct);
        var questionId = (await asked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await admin.PostAsJsonAsync($"/questions/{questionId}/answer", new { answer = "Serializable mode" }, Ct);
        await admin.PatchAsJsonAsync("/issues/PLAN-3", new { result = "The migration is complete" }, Ct);

        static string[] Keys(JsonElement page) =>
            [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()!)];

        using var identifierResponse = await admin.GetAsync("/issues?q=claim-expired", Ct);
        Assert.True(identifierResponse.IsSuccessStatusCode, string.Join("\n", instance.Errors));
        Assert.Equal(["PLAN-1"], Keys(await identifierResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        Assert.Equal(["PLAN-2"], Keys(await admin.GetFromJsonAsync<JsonElement>("/issues?q=%22for%20update%22", Ct)));
        Assert.Equal(["PLAN-1"], Keys(await admin.GetFromJsonAsync<JsonElement>("/issues?q=flaky", Ct)));
        Assert.Equal(["PLAN-2"], Keys(await admin.GetFromJsonAsync<JsonElement>("/issues?q=isolation", Ct)));
        Assert.Equal(["PLAN-3"], Keys(await admin.GetFromJsonAsync<JsonElement>("/issues?q=migration", Ct)));
        Assert.Equal(["PLAN-2", "PLAN-3"], Keys(await admin.GetFromJsonAsync<JsonElement>("/issues?sort=created&q=mode%20OR%20migration", Ct)));

        var questions = await admin.GetFromJsonAsync<JsonElement>("/questions?project=PLAN&open=false&q=serializable", Ct);
        Assert.Equal(["Which isolation level?"], questions.GetProperty("items").EnumerateArray().Select(q => q.GetProperty("question").GetString()));

        var empty = await admin.GetFromJsonAsync<JsonElement>("/issues?q=%20%20", Ct);
        Assert.Equal(3, empty.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Reading_a_question_waits_for_its_answer_and_returns_open_at_the_deadline()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "A" });
        using var agent = await Agent(instance, admin, "one");
        using var asked = await agent.PostAsJsonAsync("/issues/PLAN-1/questions", new { question = "Which way?" }, Ct);
        var id = (await asked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var waiting = agent.GetAsync($"/questions/{id}?wait=5", Ct);
        await Task.Delay(100, Ct);
        using var answered = await admin.PostAsJsonAsync($"/questions/{id}/answer", new { answer = "This way." }, Ct);
        using var response = await waiting.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var question = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("This way.", question.GetProperty("answer").GetString());

        using var secondAsked = await agent.PostAsJsonAsync("/issues/PLAN-1/questions", new { question = "And then?" }, Ct);
        var secondId = (await secondAsked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var started = DateTimeOffset.UtcNow;
        var stillOpen = await agent.GetFromJsonAsync<JsonElement>($"/questions/{secondId}?wait=1", Ct);
        Assert.Equal(JsonValueKind.Null, stillOpen.GetProperty("answer").ValueKind);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(750));

        await ProjectEndpointTests.Problem(await agent.GetAsync($"/questions/{id}?wait=0", Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(await agent.GetAsync($"/questions/{id}?wait=3601", Ct), HttpStatusCode.UnprocessableEntity, "wait-too-long");
        await ProjectEndpointTests.Problem(await agent.GetAsync($"/questions/{Guid.NewGuid()}?wait=1", Ct), HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task The_history_reads_as_the_sequence_it_was()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "Before" });
        using var agent = await Agent(instance, admin, "one");

        await agent.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);
        await agent.PatchAsJsonAsync("/issues/PLAN-1", new { title = "After", assignee = "one" }, Ct);
        await agent.PostAsJsonAsync("/issues/PLAN-1/review", new { result = "Done." }, Ct);
        await admin.PostAsJsonAsync("/issues/PLAN-1/reopen", new { comment = "Not quite." }, Ct);

        var history = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1/history", Ct);
        var entries = history.EnumerateArray().ToArray();

        Assert.Equal(
            ["created", "claim", "status", "title", "assignee", "claim", "result", "status", "status"],
            entries.Select(e => e.GetProperty("field").GetString()));
        Assert.Equal(["maintainer", "one", "one", "one", "one", "one", "one", "one", "maintainer"], entries.Select(e => e.GetProperty("actor").GetProperty("name").GetString()));

        // Identities in claim and assignee are rendered, not ids.
        Assert.Equal("one", entries[1].GetProperty("new_value").GetProperty("name").GetString());
        Assert.Equal("agent", entries[1].GetProperty("new_value").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, entries[1].GetProperty("old_value").ValueKind);
        Assert.Equal("one", entries[4].GetProperty("new_value").GetProperty("name").GetString());
        Assert.Equal("one", entries[5].GetProperty("old_value").GetProperty("name").GetString());

        Assert.Equal("Before", entries[3].GetProperty("old_value").GetString());
        Assert.Equal("After", entries[3].GetProperty("new_value").GetString());
        Assert.Equal(JsonValueKind.Null, entries[6].GetProperty("new_value").ValueKind);
        Assert.Equal("in_progress", entries[7].GetProperty("old_value").GetString());
        Assert.Equal("review", entries[7].GetProperty("new_value").GetString());
        Assert.Equal("todo", entries[8].GetProperty("new_value").GetString());
        Assert.True(entries.Zip(entries.Skip(1)).All(pair => pair.First.GetProperty("id").GetInt64() < pair.Second.GetProperty("id").GetInt64()));
    }

    /// <summary>
    /// ADR 0022: the author corrects, and a user clears up. What ADR 0020 said
    /// could not happen — a comment being rewritten — is now the point.
    /// </summary>
    [Fact]
    public async Task An_author_rewrites_their_comment_and_a_user_takes_anybodys_away()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "A" });
        using var agent = await Agent(instance, admin, "one");
        using var other = await Agent(instance, admin, "two");

        using var written = await agent.PostAsJsonAsync("/issues/PLAN-1/comments", new { body = "Halfawy." }, Ct);
        Assert.Equal(HttpStatusCode.Created, written.StatusCode);
        var comment = await written.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = comment.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, comment.GetProperty("edited_at").ValueKind);

        var before = (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct)).GetProperty("updated_at").GetDateTimeOffset();

        // Nobody else rewrites it: the byline would then say something untrue.
        await ProjectEndpointTests.Problem(
            await other.PatchAsJsonAsync($"/comments/{id}", new { body = "Mine now." }, Ct), HttpStatusCode.Forbidden, "forbidden");
        await ProjectEndpointTests.Problem(
            await admin.PatchAsJsonAsync($"/comments/{id}", new { body = "Mine now." }, Ct), HttpStatusCode.Forbidden, "forbidden");

        using var edited = await agent.PatchAsJsonAsync($"/comments/{id}", new { body = "Halfway." }, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var corrected = await edited.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Halfway.", corrected.GetProperty("body").GetString());
        Assert.Equal("one", corrected.GetProperty("author").GetProperty("name").GetString());
        Assert.NotEqual(JsonValueKind.Null, corrected.GetProperty("edited_at").ValueKind);

        // An empty body is a withdrawal, not a save.
        await ProjectEndpointTests.Problem(
            await agent.PatchAsJsonAsync($"/comments/{id}", new { body = "   " }, Ct), HttpStatusCode.BadRequest, "validation");

        var read = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct);
        Assert.Equal("Halfway.", read.GetProperty("comments")[0].GetProperty("body").GetString());
        // Both acts move the issue's version, as writing a comment already did.
        Assert.True(read.GetProperty("updated_at").GetDateTimeOffset() > before);

        // The rewritten text is what the search finds, and the old one is gone.
        Assert.Equal(["PLAN-1"], await Keys(admin, "/issues?q=Halfway"));
        Assert.Empty(await Keys(admin, "/issues?q=Halfawy"));

        // A user clears up after an agent — ADR 0013's concession, on a comment.
        using var gone = await admin.DeleteAsync($"/comments/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct)).GetProperty("comments").EnumerateArray());
        await ProjectEndpointTests.Problem(await admin.DeleteAsync($"/comments/{id}", Ct), HttpStatusCode.NotFound, "not-found");
        Assert.Empty(await Keys(admin, "/issues?q=Halfway"));

        // The history says that it happened and which comment, and no more.
        var entries = (await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1/history", Ct)).EnumerateArray()
            .Where(entry => entry.GetProperty("field").GetString() == "comment").ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal("edited", entries[0].GetProperty("note").GetString());
        Assert.Equal(id.ToString(), entries[0].GetProperty("new_value").GetString());
        Assert.Equal("withdrawn", entries[1].GetProperty("note").GetString());
        Assert.Equal(id.ToString(), entries[1].GetProperty("old_value").GetString());
        Assert.DoesNotContain("Halfawy", (await admin.GetStringAsync("/issues/PLAN-1/history", Ct)));
    }

    /// <summary>An agent takes away only its own, and the issue's state is what it hangs on.</summary>
    [Fact]
    public async Task An_agent_takes_away_only_its_own_comment()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Project(instance);
        await Issues(admin, new { title = "A" });
        using var agent = await Agent(instance, admin, "one");
        using var other = await Agent(instance, admin, "two");

        using var written = await agent.PostAsJsonAsync("/issues/PLAN-1/comments", new { body = "Mine." }, Ct);
        var id = (await written.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        await ProjectEndpointTests.Problem(
            await other.DeleteAsync($"/comments/{id}", Ct), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.NoContent, (await agent.DeleteAsync($"/comments/{id}", Ct)).StatusCode);

        await ProjectEndpointTests.Problem(
            await admin.PatchAsJsonAsync($"/comments/{Guid.NewGuid()}", new { body = "Nothing." }, Ct),
            HttpStatusCode.NotFound,
            "not-found");
    }

    private static async Task<string[]> Keys(HttpClient client, string url)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(url, Ct);
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()!)];
    }

    private static async Task<string[]> NextKeys(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()!)];
    }

    private static async Task<HttpClient> Project(AnInstance instance)
    {
        var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var project = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        return admin;
    }

    private static async Task Issues(HttpClient admin, params object[] issues)
    {
        using var created = await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private static async Task<HttpClient> Agent(AnInstance instance, HttpClient admin, string name)
    {
        using var created = await admin.PostAsJsonAsync("/agents", new { name }, Ct);
        return instance.ClientWith((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());
    }
}
