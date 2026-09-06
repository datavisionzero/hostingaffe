using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Domain.Epics;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The issue without its acts (<c>docs/api.md</c>): the transactional bulk
/// create, the two shapes, the cursor that holds under a concurrent writer, the
/// guarded change, and the edges.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class IssueEndpointTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seven_wired_up_issues_round_trip_as_seven_complete_issues()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "cut-1" }, Ct);

        // An epic, closed, to be reopened by the attachment (VISION 7).
        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var project = await context.Projects.SingleAsync(Ct);
            var author = await context.Users.SingleAsync(Ct);
            var epic = Epic.Create(project.Id, 2, "Backend and data model", author.Id, Migrated.Now);
            context.Epics.Add(epic);
            await context.SaveChangesAsync(Ct);
            await context.Database.ExecuteSqlRawAsync("update epic set status = 'closed', closed_at = now() where id = {0}", [epic.Id], Ct);
        }

        var body = new
        {
            project = "PLAN",
            issues = new object[]
            {
                new { @ref = "schema", title = "The schema", description = "Every table.", priority = 3, ready = true, labels = new[] { "feature", "cut-1" }, epic = "PLAN-E2" },
                new { @ref = "identity", title = "Identity and bootstrap", blocked_by = new[] { "schema" } },
                new { @ref = "contract", title = "The contract", blocked_by = new[] { "identity" } },
                new { @ref = "users", title = "Users, agents, tokens", blocked_by = new[] { "identity" }, assignee = "maintainer" },
                new { @ref = "projects", title = "Projects and labels", blocked_by = new[] { "identity" } },
                new { @ref = "issues", title = "Creating and reading issues", blocked_by = new[] { "projects" }, blocks = new[] { "switch" } },
                new { @ref = "switch", title = "The switch-over", status = "backlog" },
            },
        };

        using var created = await admin.PostAsJsonAsync("/issues", body, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var items = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(Enumerable.Range(1, 7).Select(n => $"PLAN-{n}"), items.Select(i => i.GetProperty("key").GetString()));
        var schema = items[0];
        Assert.Equal(3, schema.GetProperty("priority").GetInt32());
        Assert.True(schema.GetProperty("ready").GetBoolean());
        Assert.Equal(["cut-1", "feature"], schema.GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        Assert.Equal("PLAN-E2", schema.GetProperty("epic").GetProperty("key").GetString());
        Assert.Equal("open", schema.GetProperty("epic").GetProperty("status").GetString());
        Assert.Equal("maintainer", schema.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal(["bug", "chore", "cut-1", "feature"], schema.GetProperty("project_context").GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        Assert.Equal(["PLAN-2"], schema.GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("key").GetString()));

        var identity = items[1];
        Assert.Equal(["PLAN-1"], identity.GetProperty("blocked_by").EnumerateArray().Select(b => b.GetProperty("key").GetString()));
        Assert.Equal(1, identity.GetProperty("open_blockers").GetInt32());
        Assert.Equal(["PLAN-3", "PLAN-4", "PLAN-5"], identity.GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("key").GetString()));

        Assert.Equal("maintainer", items[3].GetProperty("assignee").GetProperty("name").GetString());
        Assert.Equal("backlog", items[6].GetProperty("status").GetString());
        Assert.Equal(["PLAN-6"], items[6].GetProperty("blocked_by").EnumerateArray().Select(b => b.GetProperty("key").GetString()));
        Assert.Equal("todo", items[5].GetProperty("status").GetString());

        // Read back complete, and listed slim.
        var read = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-2", Ct);
        Assert.Equal("Identity and bootstrap", read.GetProperty("title").GetString());
        Assert.True(read.TryGetProperty("description", out _));

        var listed = await admin.GetFromJsonAsync<JsonElement>("/issues?project=PLAN&sort=created&order=asc", Ct);
        Assert.Equal(7, listed.GetProperty("total").GetInt32());
        Assert.False(listed.GetProperty("has_more").GetBoolean());
        var first = listed.GetProperty("items")[0];
        Assert.False(first.TryGetProperty("description", out _));
        Assert.Equal(["cut-1", "feature"], first.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal("PLAN-E2", first.GetProperty("epic").GetString());
        Assert.Equal(0, first.GetProperty("open_sub_issues").GetInt32());
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{6}Z$", first.GetProperty("created_at").GetString());

        // History was written: a birth per issue, the labels, the edges.
        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        Assert.Equal(7, await reader.History.CountAsync(h => h.Field == "created", Ct));
        Assert.Equal(6, await reader.History.CountAsync(h => h.Field == "blocked_by", Ct));
        Assert.Equal(1, await reader.History.CountAsync(h => h.EpicId != null, Ct));
    }

    [Fact]
    public async Task A_cycle_among_the_new_issues_refuses_the_whole_request()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);

        var body = new
        {
            project = "PLAN",
            issues = new object[]
            {
                new { @ref = "a", title = "A", blocked_by = new[] { "c" } },
                new { @ref = "b", title = "B", blocked_by = new[] { "a" } },
                new { @ref = "c", title = "C", blocked_by = new[] { "b" } },
            },
        };

        using var refused = await admin.PostAsJsonAsync("/issues", body, Ct);
        var problem = await ProjectEndpointTests.Problem(refused, HttpStatusCode.UnprocessableEntity, "cycle");
        Assert.True(problem.GetProperty("path").GetArrayLength() >= 3);

        // Nothing was created, and the keys were not spent.
        var listed = await admin.GetFromJsonAsync<JsonElement>("/issues?project=PLAN", Ct);
        Assert.Equal(0, listed.GetProperty("total").GetInt32());

        using var next = await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new[] { new { title = "First for real" } } }, Ct);
        Assert.Equal("PLAN-1", (await next.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items")[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task Three_hundred_issues_page_without_a_gap_or_a_duplicate_while_another_writer_inserts()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);

        for (var batch = 0; batch < 3; batch++)
        {
            var issues = Enumerable.Range(0, 100).Select(i => new { title = $"Issue {batch * 100 + i}", priority = i % 5 }).ToArray();
            using var created = await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues }, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var originals = Enumerable.Range(1, 300).Select(n => $"PLAN-{n}").ToHashSet();
        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var url = "/issues?project=PLAN&sort=priority&limit=50" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await admin.GetFromJsonAsync<JsonElement>(url, Ct);
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()!));
            cursor = page.GetProperty("has_more").GetBoolean() ? page.GetProperty("next_cursor").GetString() : null;
            pages++;

            // Another writer between every page: seven more, with priorities
            // that land ahead of and behind the cursor.
            using var more = await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = Enumerable.Range(0, 7).Select(i => new { title = $"Late {pages}-{i}", priority = i % 5 }).ToArray() }, Ct);
            Assert.Equal(HttpStatusCode.Created, more.StatusCode);
        }
        while (cursor is not null);

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.True(originals.IsSubsetOf(seen), $"{originals.Except(seen).Count()} of the original issues never appeared.");
        Assert.True(pages >= 6);

        // The cursor is bound to its filters and sort.
        var firstPage = await admin.GetFromJsonAsync<JsonElement>("/issues?project=PLAN&sort=priority&limit=50", Ct);
        var bound = firstPage.GetProperty("next_cursor").GetString()!;
        await ProjectEndpointTests.Problem(await admin.GetAsync($"/issues?project=PLAN&sort=created&cursor={Uri.EscapeDataString(bound)}", Ct), HttpStatusCode.BadRequest, "cursor-invalid");
        await ProjectEndpointTests.Problem(await admin.GetAsync("/issues?limit=201", Ct), HttpStatusCode.BadRequest, "validation");
    }

    // The decision of PLAN-19: a list that groups by epic groups by sorting.
    // A group that opens a second time on page two is not a group, it is a
    // display error — so this pages the whole list and asks where each group
    // begins and ends, rather than looking at one page.
    [Fact]
    public async Task Sorting_by_epic_opens_every_group_exactly_once_across_the_pages()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);

        // Eleven epics, so that E10 and E11 have to sort behind E9 rather than
        // where a plain comparison of the key string would put them.
        for (var e = 1; e <= 11; e++)
        {
            using var epic = await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = $"Epic {e}" }, Ct);
            Assert.Equal(HttpStatusCode.Created, epic.StatusCode);
        }

        // Twelve issues per epic and twelve under none, in an order that has
        // nothing to do with the one they are expected back in.
        var issues = new List<object>();
        for (var i = 0; i < 12; i++)
        {
            for (var e = 11; e >= 1; e--)
            {
                issues.Add(new { title = $"E{e} #{i}", epic = $"PLAN-E{e}", priority = (i + e) % 5 });
            }

            issues.Add(new { title = $"Loose #{i}", priority = i % 5 });
        }

        foreach (var batch in issues.Chunk(72))
        {
            using var created = await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = batch }, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var seen = new List<JsonElement>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var url = "/issues?project=PLAN&sort=epic&limit=25" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await admin.GetFromJsonAsync<JsonElement>(url, Ct);
            seen.AddRange(page.GetProperty("items").EnumerateArray());
            cursor = page.GetProperty("has_more").GetBoolean() ? page.GetProperty("next_cursor").GetString() : null;
            pages++;
        }
        while (cursor is not null);

        Assert.True(pages >= 6, $"{pages} pages is not enough to cross a group boundary in the middle of a page.");
        var keys = seen.Select(i => i.GetProperty("key").GetString()!).ToArray();
        Assert.Equal(12 * 12, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct().Count());

        // Every group is one unbroken run, in key order, with the issues under
        // no epic as the last run of all.
        var groups = seen.Select(i => i.GetProperty("epic").GetString()).ToArray();
        var opened = new List<string?>();
        for (var i = 0; i < groups.Length; i++)
        {
            if (i == 0 || groups[i] != groups[i - 1])
            {
                opened.Add(groups[i]);
            }
        }

        Assert.Equal(
            [.. Enumerable.Range(1, 11).Select(e => $"PLAN-E{e}"), null],
            opened);
        Assert.Equal(12, groups.Count(g => g is null));

        // Within a group: priority descending, then the number.
        foreach (var group in opened)
        {
            var inside = seen
                .Where(i => i.GetProperty("epic").GetString() == group)
                .Select(i => (Priority: i.GetProperty("priority").GetInt32(), Number: int.Parse(i.GetProperty("key").GetString()!.Split('-')[1], System.Globalization.CultureInfo.InvariantCulture)))
                .ToArray();

            Assert.Equal([.. inside.OrderByDescending(i => i.Priority).ThenBy(i => i.Number)], inside);
        }

        // A cursor issued for this sort is still refused by another one.
        var first = await admin.GetFromJsonAsync<JsonElement>("/issues?project=PLAN&sort=epic&limit=25", Ct);
        var bound = first.GetProperty("next_cursor").GetString()!;
        await ProjectEndpointTests.Problem(
            await admin.GetAsync($"/issues?project=PLAN&sort=priority&cursor={Uri.EscapeDataString(bound)}", Ct),
            HttpStatusCode.BadRequest,
            "cursor-invalid");
        await ProjectEndpointTests.Problem(await admin.GetAsync("/issues?project=PLAN&sort=nonsense", Ct), HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task The_filters_narrow_the_list()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        using var createdAgent = await admin.PostAsJsonAsync("/agents", new { name = "quiet-otter-42" }, Ct);
        using var agent = instance.ClientWith((await createdAgent.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetProperty("secret").GetString());

        await admin.PostAsJsonAsync("/issues", new
        {
            project = "PLAN",
            issues = new object[]
            {
                new { @ref = "a", title = "A", priority = 4, ready = true, labels = new[] { "bug" }, assignee = "maintainer" },
                new { @ref = "b", title = "B", priority = 1, labels = new[] { "feature" }, blocked_by = new[] { "a" } },
                new { @ref = "c", title = "C", status = "backlog", assignee = "quiet-otter-42" },
            },
        }, Ct);

        Assert.Equal(["PLAN-1"], await Keys(admin, "/issues?project=PLAN&label=bug"));
        Assert.Equal(["PLAN-1"], await Keys(admin, "/issues?project=PLAN&ready=true"));
        Assert.Equal(["PLAN-1"], await Keys(admin, "/issues?project=PLAN&priority_min=2"));
        Assert.Equal(["PLAN-1"], await Keys(admin, "/issues?project=PLAN&assignee=me"));
        Assert.Equal(["PLAN-3"], await Keys(agent, "/issues?project=PLAN&assignee=me"));
        Assert.Equal(["PLAN-2"], await Keys(admin, "/issues?project=PLAN&assignee=none"));
        Assert.Equal(["PLAN-3"], await Keys(admin, "/issues?project=PLAN&status=backlog"));
        Assert.Equal(["PLAN-2", "PLAN-1"], await Keys(admin, "/issues?project=PLAN&status=todo&status=in_progress&sort=created&order=desc"));
        Assert.Equal(["PLAN-2"], await Keys(admin, "/issues?project=PLAN&blocked=true"));
        // PLAN-2 waits on PLAN-1, which is open; the other two wait on nothing.
        Assert.Equal(["PLAN-1", "PLAN-3"], await Keys(admin, "/issues?project=PLAN&blocked=false&epic=none&claimed=false&author=maintainer&sort=created"));
        Assert.Equal(["PLAN-1", "PLAN-2", "PLAN-3"], await Keys(admin, "/issues?project=PLAN&sort=priority"));
        Assert.Empty(await Keys(admin, "/issues?project=PLAN&deleted=true"));
    }

    [Fact]
    public async Task A_patch_changes_what_is_present_and_a_stale_one_is_refused_with_the_current_issue()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        await admin.PostAsJsonAsync("/projects/PLAN/labels", new { name = "cut-1" }, Ct);
        await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new[] { new { title = "Before", description = "Old text", labels = new[] { "bug" } } } }, Ct);

        var before = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct);
        var version = before.GetProperty("updated_at").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/issues/PLAN-1")
        {
            Content = JsonContent.Create(new { title = "After", description = (string?)null, priority = 2, ready = true, assignee = "maintainer", labels = new[] { "feature", "cut-1" } }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var changed = await admin.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("After", after.GetProperty("title").GetString());
        Assert.Equal(string.Empty, after.GetProperty("description").GetString());
        Assert.Equal(2, after.GetProperty("priority").GetInt32());
        Assert.True(after.GetProperty("ready").GetBoolean());
        Assert.Equal("maintainer", after.GetProperty("assignee").GetProperty("name").GetString());
        Assert.Equal(["cut-1", "feature"], after.GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        Assert.NotEqual(version, after.GetProperty("updated_at").GetString());

        // The old version again: stale, and the current issue comes with it.
        using var staleRequest = new HttpRequestMessage(HttpMethod.Patch, "/issues/PLAN-1") { Content = JsonContent.Create(new { title = "Later" }) };
        staleRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var stale = await admin.SendAsync(staleRequest, Ct);
        var problem = await ProjectEndpointTests.Problem(stale, HttpStatusCode.PreconditionFailed, "stale");
        Assert.Equal("After", problem.GetProperty("current").GetProperty("title").GetString());

        // Two labels of one group in the set; a label the project lacks; the status.
        await ProjectEndpointTests.Problem(await admin.PatchAsJsonAsync("/issues/PLAN-1", new { labels = new[] { "bug", "feature" } }, Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(await admin.PatchAsJsonAsync("/issues/PLAN-1", new { labels = new[] { "nope" } }, Ct), HttpStatusCode.UnprocessableEntity, "unknown-label");
        await ProjectEndpointTests.Problem(await admin.PatchAsJsonAsync("/issues/PLAN-1", new { status = "done" }, Ct), HttpStatusCode.UnprocessableEntity, "transition");
        await ProjectEndpointTests.Problem(await admin.GetAsync("/issues/PLAN-99", Ct), HttpStatusCode.NotFound, "not-found");

        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        // The birth wrote `created` and one `label`; the patch wrote the rest:
        // `bug` off, `feature` and `cut-1` on, and the six fields.
        var fields = await reader.History.Where(h => h.Field != "created").Select(h => h.Field).ToListAsync(Ct);
        Assert.Equal(["assignee", "description", "label", "label", "label", "label", "priority", "ready", "title"], fields.Order().ToArray());
    }

    /// <summary>
    /// <em>Triage required</em> decides what <c>next</c> hands out and nothing
    /// else: an agent writes <c>ready</c> in both directions, and the history
    /// says who did (ADR 0019).
    /// </summary>
    [Fact]
    public async Task Under_triage_required_an_agent_writes_ready_both_ways_and_the_flag_still_decides_the_supply()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        await admin.PatchAsJsonAsync("/projects/PLAN", new { triage_required = true }, Ct);
        using var createdAgent = await admin.PostAsJsonAsync("/agents", new { }, Ct);
        var identity = await createdAgent.Content.ReadFromJsonAsync<JsonElement>(Ct);
        using var agent = instance.ClientWith(identity.GetProperty("token").GetProperty("secret").GetString());

        // Whoever writes the issues says which of them are concrete.
        using var created = await agent.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new object[] { new { title = "Concrete", ready = true }, new { title = "Thin" } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True((await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items")[0].GetProperty("ready").GetBoolean());

        // And takes it back, and puts it back, without a user in between.
        Assert.Equal(HttpStatusCode.OK, (await agent.PatchAsJsonAsync("/issues/PLAN-1", new { ready = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PatchAsJsonAsync("/issues/PLAN-1", new { ready = true }, Ct)).StatusCode);

        // The switch is untouched where it works: without the flag, nothing is handed out.
        var page = await agent.GetFromJsonAsync<JsonElement>("/projects/PLAN/next", Ct);
        Assert.Equal(["PLAN-1"], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
        Assert.Equal(1, page.GetProperty("reasons").GetProperty("not_ready").GetInt32());

        // Who set it is in the history, which is where the question is answered now.
        await using var reader = Migrated.ContextFor(instance.ConnectionString);
        var agentId = identity.GetProperty("id").GetGuid();
        var wrote = await reader.History.Where(h => h.Field == "ready").Select(h => h.ActorId).ToListAsync(Ct);
        Assert.Equal([agentId, agentId], wrote);
    }

    [Fact]
    public async Task The_edges_add_and_remove_a_label_and_a_blocker()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new object[] { new { title = "A", labels = new[] { "bug" } }, new { title = "B" }, new { title = "C" } } }, Ct);

        // A label of the same group replaces the other.
        using var relabelled = await admin.PostAsync("/issues/PLAN-1/labels/feature", null, Ct);
        Assert.Equal(HttpStatusCode.OK, relabelled.StatusCode);
        Assert.Equal(["feature"], (await relabelled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        using var unlabelled = await admin.DeleteAsync("/issues/PLAN-1/labels/feature", Ct);
        Assert.Empty((await unlabelled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("labels").EnumerateArray());
        await ProjectEndpointTests.Problem(await admin.PostAsync("/issues/PLAN-1/labels/nope", null, Ct), HttpStatusCode.UnprocessableEntity, "unknown-label");

        // A blocks B blocks C; C blocking A would close the cycle.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/issues/PLAN-2/blocked-by/PLAN-1", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/issues/PLAN-3/blocked-by/PLAN-2", null, Ct)).StatusCode);
        var problem = await ProjectEndpointTests.Problem(await admin.PostAsync("/issues/PLAN-1/blocked-by/PLAN-3", null, Ct), HttpStatusCode.UnprocessableEntity, "cycle");
        Assert.Equal(["PLAN-1", "PLAN-3", "PLAN-2", "PLAN-1"], problem.GetProperty("path").EnumerateArray().Select(p => p.GetString()));
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct)).GetProperty("blocked_by").EnumerateArray());

        using var unblocked = await admin.DeleteAsync("/issues/PLAN-3/blocked-by/PLAN-2", Ct);
        Assert.Equal(HttpStatusCode.OK, unblocked.StatusCode);
        Assert.Empty((await unblocked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("blocked_by").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/issues/PLAN-1/blocked-by/PLAN-3", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_deleted_issue_reads_as_deleted_and_lists_only_under_the_switch()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new[] { new { title = "Gone" }, new { title = "Here" } } }, Ct);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var user = await context.Users.SingleAsync(Ct);
            await context.Database.ExecuteSqlRawAsync("update issue set deleted_at = now(), deleted_by = {0} where number = 1", [user.Id], Ct);
        }

        var problem = await ProjectEndpointTests.Problem(await admin.GetAsync("/issues/PLAN-1", Ct), HttpStatusCode.NotFound, "deleted");
        Assert.True(problem.TryGetProperty("restorable_until", out _));
        Assert.Equal(["PLAN-2"], await Keys(admin, "/issues?project=PLAN"));

        var deleted = await admin.GetFromJsonAsync<JsonElement>("/issues?project=PLAN&deleted=true", Ct);
        var item = Assert.Single(deleted.GetProperty("items").EnumerateArray());
        Assert.Equal("PLAN-1", item.GetProperty("key").GetString());
        Assert.Equal("maintainer", item.GetProperty("deleted_by").GetProperty("name").GetString());
    }

    /// <summary>
    /// What a predecessor decided travels with the ticket (VISION 15.5), so
    /// that an agent does not have to fetch a blocker to learn why it was one.
    /// </summary>
    [Fact]
    public async Task A_blocker_carries_its_result_and_a_blocked_issue_does_not()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        await admin.PostAsJsonAsync(
            "/issues",
            new
            {
                project = "PLAN",
                issues = new object[]
                {
                    new { @ref = "schema", title = "The schema" },
                    new { @ref = "contract", title = "The contract", blocked_by = new[] { "schema" } },
                },
            },
            Ct);

        // While the blocker is open it has decided nothing.
        var open = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-2", Ct);
        Assert.Equal(JsonValueKind.Null, open.GetProperty("blocked_by")[0].GetProperty("result").ValueKind);

        using var closed = await admin.PostAsJsonAsync(
            "/issues/PLAN-1/close", new { status = "done", result = "Four tables, and the view derives the status." }, Ct);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var after = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-2", Ct);
        var blocker = after.GetProperty("blocked_by")[0];
        Assert.Equal("PLAN-1", blocker.GetProperty("key").GetString());
        Assert.False(blocker.GetProperty("open").GetBoolean());
        Assert.Equal("Four tables, and the view derives the status.", blocker.GetProperty("result").GetString());

        // The other direction carries none: a successor has decided nothing yet,
        // and what it decides is no business of the work here.
        var blockerRead = await admin.GetFromJsonAsync<JsonElement>("/issues/PLAN-1", Ct);
        Assert.Equal("PLAN-2", blockerRead.GetProperty("blocks")[0].GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, blockerRead.GetProperty("blocks")[0].GetProperty("result").ValueKind);
    }

    /// <summary>
    /// The three states of a <c>PATCH</c> (<c>docs/api.md</c>): absent leaves a
    /// field alone, present as <c>null</c> clears it, present with a value
    /// writes it. Pinned here because a client read the first two as one and
    /// wrote away a description, an epic and an assignee with a press meant for
    /// one flag.
    /// </summary>
    [Fact]
    public async Task An_absent_field_is_left_alone_and_one_present_as_null_is_cleared()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        await admin.PostAsJsonAsync("/epics", new { project = "PLAN", title = "Theme" }, Ct);
        await admin.PostAsJsonAsync(
            "/issues",
            new
            {
                project = "PLAN",
                issues = new[] { new { title = "The work", description = "Four columns.", epic = "PLAN-E1", assignee = "maintainer" } },
            },
            Ct);

        // One flag, and nothing else named: everything else stands.
        using var flagged = await admin.PatchAsJsonAsync("/issues/PLAN-1", new { ready = true }, Ct);
        var after = await flagged.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(after.GetProperty("ready").GetBoolean());
        Assert.Equal("Four columns.", after.GetProperty("description").GetString());
        Assert.Equal("PLAN-E1", after.GetProperty("epic").GetProperty("key").GetString());
        Assert.Equal("maintainer", after.GetProperty("assignee").GetProperty("name").GetString());

        // The same fields named as null are the other act entirely.
        using var cleared = await admin.PatchAsJsonAsync(
            "/issues/PLAN-1",
            new { description = (string?)null, epic = (string?)null, assignee = (string?)null },
            Ct);
        var empty = await cleared.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(string.Empty, empty.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("epic").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("assignee").ValueKind);
        Assert.True(empty.GetProperty("ready").GetBoolean());
    }

    private static async Task<HttpClient> Ready(AnInstance instance)
    {
        var admin = instance.ClientWith(AnInstance.BootstrapToken);
        using var project = await admin.PostAsJsonAsync("/projects", new { key = "PLAN", name = "hostingaffe" }, Ct);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        return admin;
    }

    [Fact]
    public async Task The_list_carries_an_etag_and_wait_holds_it_until_the_filtered_page_changes()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var admin = await Ready(instance);
        await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new[] { new { title = "A" } } }, Ct);

        using var initial = await admin.GetAsync("/issues?project=PLAN&status=in_progress", Ct);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var etag = initial.Headers.ETag?.ToString();
        Assert.NotNull(etag);
        Assert.Equal(0, (await initial.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("total").GetInt32());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/issues?project=PLAN&status=in_progress&wait=5");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var waiting = admin.SendAsync(request, Ct);
        await Task.Delay(100, Ct);

        // A claim is what puts an issue into `in_progress`; the wake channel is
        // the project's and says nothing about which list wants to hear it.
        using var claimed = await admin.PostAsJsonAsync("/issues/PLAN-1/claim", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);

        using var changed = await waiting.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var page = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        var changedTag = changed.Headers.ETag?.ToString();
        Assert.NotNull(changedTag);
        Assert.NotEqual(etag, changedTag);

        // An unrelated change in the same project wakes this list, which asks
        // its question again and answers 304 with the validator it already had.
        using var unchangedRequest = new HttpRequestMessage(HttpMethod.Get, "/issues?project=PLAN&status=in_progress&wait=2");
        unchangedRequest.Headers.TryAddWithoutValidation("If-None-Match", changedTag);
        var quiet = admin.SendAsync(unchangedRequest, Ct);
        await Task.Delay(100, Ct);
        await admin.PostAsJsonAsync("/issues", new { project = "PLAN", issues = new[] { new { title = "B" } } }, Ct);

        using var unchanged = await quiet.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Equal(changedTag, unchanged.Headers.ETag?.ToString());

        // The channel is one project's, so a list that spans them all may not wait.
        await ProjectEndpointTests.Problem(
            await admin.GetAsync("/issues?wait=5", Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(
            await admin.GetAsync("/issues?project=PLAN&wait=0", Ct), HttpStatusCode.BadRequest, "validation");
        await ProjectEndpointTests.Problem(
            await admin.GetAsync("/issues?project=PLAN&wait=3601", Ct), HttpStatusCode.UnprocessableEntity, "wait-too-long");
    }

    private static async Task<string[]> Keys(HttpClient client, string url)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(url, Ct);
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()!)];
    }
}
