using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The issue rows. Reads start at <c>issue_read</c> — the deleted issue absent,
/// the expired claim gone — and writes take the row <c>for update</c>
/// (<c>docs/storage.md</c>, What is derived on read).
/// </summary>
public sealed class Issues(HostingaffeDbContext context) : IIssues
{
    private const int CycleDepth = 100;

    private sealed class NeedsYouSelection
    {
        public Guid Id { get; init; }

        public int Because { get; init; }
    }

    public Task<IssueRow?> FindLiveAsync(string projectKey, int number, CancellationToken cancellationToken) =>
        Live().Where(r => r.ProjectKey == projectKey && r.Number == number).SingleOrDefaultAsync(cancellationToken);

    public Task<IssueRow?> FindDeletedAsync(string projectKey, int number, CancellationToken cancellationToken) =>
        Deleted().Where(r => r.ProjectKey == projectKey && r.Number == number).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<IssueRow>> FindLiveManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToArray();
        return list.Length == 0 ? [] : await Live().Where(r => list.Contains(r.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IssuePageRows> ListAsync(
        IssueQuery query, IssueSort sort, SortOrder order, IssuePosition? after, int limit, CancellationToken cancellationToken)
    {
        var rows = Filtered(query.Deleted ? Deleted() : Live(), query);
        var total = await rows.CountAsync(cancellationToken);

        rows = Sorted(rows, sort, order);
        if (after is not null)
        {
            rows = After(rows, sort, order, after);
        }

        var page = await rows.Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > limit;

        return new IssuePageRows(hasMore ? page[..limit] : page, total, hasMore);
    }

    // The eight conditions of VISION 10 as
    // one statement over the table, with the two derived rules repeated inline
    // — because the row it locks is the row, not the view (docs/storage.md).
    // The GET and the POST run the same text; the POST adds the lock.
    private const string Workable = """
        with derived as (
            select i.id, i.project_id, i.epic_id, i.parent_id, i.priority, i.created_at, i.number, i.assignee_id, i.ready,
                   case when i.claimed_by is not null and i.claim_expires_at is not null and i.claim_expires_at <= now()
                        then 'todo' else i.status end as status,
                   case when i.claimed_by is not null and i.claim_expires_at is not null and i.claim_expires_at <= now()
                        then null else i.claimed_by end as claimed_by
              from issue i
             where i.deleted_at is null
        )
        select d.id
          from derived d
          join issue i on i.id = d.id
         where d.project_id = {0}
           and d.status = 'todo'
           and d.claimed_by is null
           and (d.assignee_id is null or d.assignee_id = {1})
           and (not {2} or d.ready)
           -- Every condition on the candidate's own row, a second time, on the
           -- row the lock is taken on rather than on the snapshot the CTE was
           -- built from. `for update` rechecks a row another writer changed
           -- while this query ran, and that recheck can only see the quals that
           -- name `i`: a CTE is not run again for it. Without these, an issue
           -- claimed and committed in that window came back as a candidate, and
           -- `next` answered `claim-held` instead of handing out the next one —
           -- a refusal the caller cannot act on and VISION 11 does not allow.
           and i.deleted_at is null
           and i.project_id = {0}
           and (case when i.claimed_by is not null and i.claim_expires_at is not null and i.claim_expires_at <= now()
                     then 'todo' else i.status end) = 'todo'
           and (case when i.claimed_by is not null and i.claim_expires_at is not null and i.claim_expires_at <= now()
                     then null else i.claimed_by end) is null
           and (i.assignee_id is null or i.assignee_id = {1})
           and (not {2} or i.ready)
           and not exists (select 1 from question q where q.issue_id = d.id and q.answer is null)
           and not exists (select 1 from blocker b join derived f on f.id = b.blocker_id
                            where b.blocked_id = d.id and f.status not in ('done', 'canceled'))
           and not exists (select 1 from derived c where c.parent_id = d.id and c.status not in ('done', 'canceled'))
           and (d.parent_id is null or exists (
               select 1 from derived p
                where p.id = d.parent_id and p.status not in ('backlog', 'done', 'canceled')
                  and not exists (select 1 from blocker pb join derived pf on pf.id = pb.blocker_id
                                   where pb.blocked_id = p.id and pf.status not in ('done', 'canceled'))))
           and ({3}::uuid is null or d.epic_id = {3}::uuid)
           and not exists (select 1 from unnest({4}::text[]) as wanted(name)
                            where not exists (select 1 from issue_label il join label l on l.id = il.label_id
                                               where il.issue_id = d.id and l.name = wanted.name and l.deleted_at is null))
           and ({5}::text is null
                or exists (select 1 from issue_label il join label l on l.id = il.label_id
                            where il.issue_id = d.id and l.name = {5}::text and l.deleted_at is null)
                or not exists (select 1 from issue_label il join label l on l.id = il.label_id
                                where il.issue_id = d.id and l.label_group = {6}::text and l.deleted_at is null))
        """;

    // Priority first; then an epic nobody else is working in before one
    // somebody is — an issue without an epic counts as nobody; then the older
    // issue, then the number (VISION 10).
    private const string InNextOrder = """
         order by d.priority desc,
                  (d.epic_id is not null and exists (select 1 from derived o
                                                      where o.epic_id = d.epic_id and o.claimed_by is not null and o.claimed_by <> {1})) asc,
                  d.created_at asc,
                  d.number asc
         limit {7}
        """;

    public async Task<IReadOnlyList<Guid>> NextWorkableAsync(NextQuery query, int limit, bool lockOne, CancellationToken cancellationToken)
    {
        if (lockOne && context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("next locks its issue inside a transaction, or the lock is worth nothing.");
        }

        var sql = Workable + InNextOrder + (lockOne ? " for update of i skip locked" : string.Empty);

        return await context.Database
            .SqlQueryRaw<Guid>(sql.Replace("select d.id", "select d.id as \"Value\"", StringComparison.Ordinal), Parameters(query, limit))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountWorkableAsync(NextQuery query, CancellationToken cancellationToken)
    {
        var sql = "select count(*)::int as \"Value\" from (" + Workable + ") as workable";
        return (await context.Database.SqlQueryRaw<int>(sql, Parameters(query, 0)).ToListAsync(cancellationToken))[0];
    }

    public async Task<Reasons> ReasonsAsync(NextQuery query, CancellationToken cancellationToken)
    {
        var open = Live().Where(r => r.ProjectId == query.ProjectId && r.Status != IssueStatus.Done && r.Status != IssueStatus.Canceled);

        if (query.EpicId is { } epicId)
        {
            open = open.Where(r => r.EpicId == epicId);
        }

        foreach (var name in query.Labels)
        {
            open = open.Where(r => context.IssueLabels.Any(il =>
                il.IssueId == r.Id && context.Labels.Any(l => l.Id == il.LabelId && l.Name == name && l.DeletedAt == null)));
        }

        if (query.RepoLabel is { } repo)
        {
            open = open.Where(r =>
                context.IssueLabels.Any(il => il.IssueId == r.Id && context.Labels.Any(l => l.Id == il.LabelId && l.Name == repo && l.DeletedAt == null))
                || !context.IssueLabels.Any(il => il.IssueId == r.Id && context.Labels.Any(l => l.Id == il.LabelId && l.Group == Label.RepoGroup && l.DeletedAt == null)));
        }

        var live = Live();
        var rows = await open
            .Select(r => new
            {
                r.Status,
                r.Ready,
                r.AssigneeId,
                Blocked = context.Blockers.Any(b => b.BlockedId == r.Id && live.Any(f => f.Id == b.BlockerId && f.Status != IssueStatus.Done && f.Status != IssueStatus.Canceled)),
                Asking = context.Questions.Any(q => q.IssueId == r.Id && q.Answer == null),
                ParentGated = r.ParentId != null && live.Any(p => p.Id == r.ParentId &&
                    (p.Status == IssueStatus.Backlog || p.Status == IssueStatus.Done || p.Status == IssueStatus.Canceled
                     || context.Blockers.Any(b => b.BlockedId == p.Id && live.Any(f => f.Id == b.BlockerId && f.Status != IssueStatus.Done && f.Status != IssueStatus.Canceled)))),
            })
            .ToListAsync(cancellationToken);

        return new Reasons(
            rows.Count(r => r.Blocked),
            rows.Count(r => r.Asking),
            rows.Count(r => r.Status == IssueStatus.InProgress),
            rows.Count(r => r.Status == IssueStatus.Review),
            rows.Count(r => r.Status == IssueStatus.Backlog),
            query.RequireReady ? rows.Count(r => r.Status == IssueStatus.Todo && !r.Ready) : 0,
            rows.Count(r => r.Status == IssueStatus.Todo && r.AssigneeId != null && r.AssigneeId != query.CallerId),
            rows.Count(r => r.ParentGated));
    }

    // The human's four groups. `walk` follows only open blocker edges and
    // remembers its path: blocker cycles are refused on write, but the guard
    // keeps old or manually changed data from making this read recurse forever.
    //
    // Whether the instance has an agent at all is deliberately not in here. It
    // is the same for every row, so as a condition on rows it turned every
    // blocked issue in the project into a separate emergency at once — and the
    // thing to do about it is to create an agent token, which has nothing to do
    // with any of them. It is counted once per answer instead.
    private const string NeedsYouBase = """
        with recursive derived as (
            select i.id, i.project_id, i.priority, i.created_at, i.number, i.ready,
                   case when i.claimed_by is not null and i.claim_expires_at is not null and i.claim_expires_at <= now()
                        then 'todo' else i.status end as status
              from issue i
             where i.deleted_at is null
        ),
        walk (root_id, node_id, path) as (
            select blocked.id, blocker.id, array[blocked.id, blocker.id]
              from derived blocked
              join blocker edge on edge.blocked_id = blocked.id
              join derived blocker on blocker.id = edge.blocker_id
             where blocked.project_id = {0}
               and blocked.status not in ('done', 'canceled')
               and blocker.status not in ('done', 'canceled')
            union all
            select walk.root_id, blocker.id, walk.path || blocker.id
              from walk
              join blocker edge on edge.blocked_id = walk.node_id
              join derived blocker on blocker.id = edge.blocker_id
             where blocker.status not in ('done', 'canceled')
               and cardinality(walk.path) <= {1}
               and not blocker.id = any(walk.path)
        ),
        stuck as (
            select distinct walk.root_id
              from walk
              join derived terminal on terminal.id = walk.node_id
             where terminal.status = 'backlog'
                or exists (select 1 from question q where q.issue_id = terminal.id and q.answer is null)
        ),
        classified as (
            select candidate.id,
                   case
                     when exists (select 1 from question q where q.issue_id = candidate.id and q.answer is null) then 0
                     when candidate.status = 'review' then 1
                     when {2} and candidate.status = 'todo' and not candidate.ready then 2
                     when stuck.root_id is not null and candidate.status <> 'backlog' then 3
                   end as because,
                   candidate.priority, candidate.created_at, candidate.number
              from derived candidate
              left join stuck on stuck.root_id = candidate.id
             where candidate.project_id = {0}
               and candidate.status not in ('done', 'canceled')
               and (exists (select 1 from question q where q.issue_id = candidate.id and q.answer is null)
                    or candidate.status = 'review'
                    or ({2} and candidate.status = 'todo' and not candidate.ready)
                    -- A parked issue is the explicit decision that it is not up
                    -- yet, so it is not something waiting for a human. Waiting
                    -- *behind* a parked blocker still is, which is the
                    -- `terminal` clause above and stays.
                    or (stuck.root_id is not null and candidate.status <> 'backlog'))
        )
        """;

    public async Task<NeedsYouPageRows> NeedsYouAsync(
        Guid projectId, bool triageRequired, NeedsYouPosition? after, int limit, CancellationToken cancellationToken)
    {
        var parameters = NeedsYouParameters(projectId, triageRequired, after, limit + 1);
        var afterSql = after is null
            ? string.Empty
            : """
               where because > {3}
                  or (because = {3} and priority < {4})
                  or (because = {3} and priority = {4} and created_at > {5})
                  or (because = {3} and priority = {4} and created_at = {5} and number > {6})
                  or (because = {3} and priority = {4} and created_at = {5} and number = {6} and id > {7})
              """;

        var pageSql = NeedsYouBase + "select id as \"Id\", because as \"Because\" from classified " + afterSql
            + " order by because, priority desc, created_at, number, id limit {8}";
        var countSql = NeedsYouBase + "select count(*)::int as \"Value\" from classified";
        var selected = await context.Database.SqlQueryRaw<NeedsYouSelection>(pageSql, parameters)
            .ToListAsync(cancellationToken);
        var total = (await context.Database.SqlQueryRaw<int>(countSql, parameters)
            .ToListAsync(cancellationToken))[0];
        var hasMore = selected.Count > limit;
        var page = hasMore ? selected[..limit] : selected;

        // Once per answer, not once per row. Until project assignment arrives
        // in cut three every live agent can work in every project, so "agents
        // that could pick this up" is the instance's unrevoked agent tokens;
        // the count becomes project-scoped later and the shape does not change.
        var agents = await context.Tokens
            .CountAsync(t => t.Kind == IdentityKind.Agent && t.RevokedAt == null, cancellationToken);

        return new NeedsYouPageRows(
            [.. page.Select(row => new NeedsYouRow(row.Id, (NeedsYouBecause)row.Because))], total, hasMore, agents);
    }

    private static object[] NeedsYouParameters(Guid projectId, bool triageRequired, NeedsYouPosition? after, int limit) =>
    [
        projectId,
        CycleDepth,
        triageRequired,
        (int)(after?.Because ?? NeedsYouBecause.Question),
        (short)(after?.Priority ?? Priority.None),
        after?.CreatedAt ?? DateTimeOffset.UnixEpoch,
        after?.Number ?? 0,
        after?.Id ?? Guid.Empty,
        limit,
    ];

    private static object[] Parameters(NextQuery query, int limit) =>
    [
        query.ProjectId,
        query.CallerId,
        query.RequireReady,
        new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = query.EpicId.HasValue ? query.EpicId.Value : DBNull.Value },
        new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = query.Labels.ToArray() },
        new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = query.RepoLabel is null ? DBNull.Value : query.RepoLabel },
        new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = Label.RepoGroup },
        limit,
    ];

    // Two statements, deliberately. The lock is its own statement, because a
    // `for update` inside the subquery EF Core composes around a raw select
    // waits for the other writer and then hands back the row as it was before
    // that writer committed — which is how two claimants both won, once. The
    // load after the lock is an ordinary query with a fresh snapshot, and sees
    // what the writer it waited for wrote.
    public async Task<Issue?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        var locked = await context.Database.ExecuteSqlRawAsync(
            "select id from issue where id = {0} and deleted_at is null for update", [id], cancellationToken);

        return await context.Issues.SingleOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, cancellationToken);
    }

    public async Task<Issue?> LoadDeletedForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        await context.Database.ExecuteSqlRawAsync(
            "select id from issue where id = {0} and deleted_at is not null for update", [id], cancellationToken);

        return await context.Issues.SingleOrDefaultAsync(i => i.Id == id && i.DeletedAt != null, cancellationToken);
    }

    public async Task<IReadOnlyList<IssueLabelRow>> LabelsOfAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken) =>
        await (
            from il in context.IssueLabels
            join l in context.Labels on il.LabelId equals l.Id
            where issueIds.Contains(il.IssueId) && l.DeletedAt == null
            select new IssueLabelRow(il.IssueId, l)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EdgeRow>> BlockersOfAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken) =>
        await (
            from b in context.Blockers
            where issueIds.Contains(b.BlockedId)
            join far in Live() on b.BlockerId equals far.Id
            orderby far.ProjectKey, far.Number
            select new EdgeRow(b.BlockedId, far)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EdgeRow>> BlockedByEachAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken) =>
        await (
            from b in context.Blockers
            where issueIds.Contains(b.BlockerId)
            join far in Live() on b.BlockedId equals far.Id
            orderby far.ProjectKey, far.Number
            select new EdgeRow(b.BlockerId, far)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> OpenQuestionCountsAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken) =>
        await context.Questions
            .Where(q => issueIds.Contains(q.IssueId) && q.Answer == null)
            .GroupBy(q => q.IssueId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> OpenSubIssueCountsAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken) =>
        await Live().Where(i => i.ParentId != null && issueIds.Contains(i.ParentId.Value)
                                      && i.Status != IssueStatus.Done && i.Status != IssueStatus.Canceled)
            .GroupBy(i => i.ParentId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<IReadOnlyList<IssueRow>> SubIssuesOfAsync(Guid issueId, CancellationToken cancellationToken) =>
        await Live().Where(i => i.ParentId == issueId).OrderBy(i => i.Number).ToListAsync(cancellationToken);

    public Task<bool> HasSubIssuesAsync(Guid issueId, CancellationToken cancellationToken) =>
        context.Issues.AnyAsync(i => i.ParentId == issueId, cancellationToken);

    public async Task<IReadOnlyList<Comment>> CommentsOfAsync(Guid issueId, CancellationToken cancellationToken) =>
        await context.Comments.Where(c => c.IssueId == issueId).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Question>> QuestionsOfAsync(Guid issueId, CancellationToken cancellationToken) =>
        await context.Questions.Where(q => q.IssueId == issueId).OrderBy(q => q.AskedAt).ThenBy(q => q.Id).ToListAsync(cancellationToken);

    public void Add(Issue issue) => context.Issues.Add(issue);

    public void Add(Comment comment) => context.Comments.Add(comment);

    public Task<Comment?> FindCommentAsync(Guid id, CancellationToken cancellationToken) =>
        context.Comments.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Remove(Comment comment) => context.Comments.Remove(comment);

    public void Add(Question question) => context.Questions.Add(question);

    public Task<Question?> FindQuestionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Questions.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);

    public Task<Question?> FindQuestionForReadAsync(Guid id, CancellationToken cancellationToken) =>
        context.Questions.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task<QuestionPageRows> ListQuestionsAsync(
        QuestionQuery query, QuestionPosition? after, int limit, CancellationToken cancellationToken)
    {
        var rows =
            from q in context.Questions
            join i in Live() on q.IssueId equals i.Id
            select new { Question = q, i.ProjectId, IssueKey = i.ProjectKey + "-" + i.Number, i.Title };

        var allowedProjectIds = query.AllowedProjectIds.ToArray();
        rows = rows.Where(r => allowedProjectIds.Contains(r.ProjectId));

        if (query.ProjectId is { } projectId)
        {
            rows = rows.Where(r => r.ProjectId == projectId);
        }

        if (query.IssueId is { } issueId)
        {
            rows = rows.Where(r => r.Question.IssueId == issueId);
        }

        if (query.Open is { } open)
        {
            rows = open ? rows.Where(r => r.Question.Answer == null) : rows.Where(r => r.Question.Answer != null);
        }

        if (query.Search is { } search)
        {
            rows = rows.Where(r => EF.Property<NpgsqlTsVector>(r.Question, "Search")
                .Matches(EF.Functions.WebSearchToTsQuery("simple", search)));
        }

        var total = await rows.CountAsync(cancellationToken);

        rows = rows.OrderBy(r => r.Question.AskedAt).ThenBy(r => r.Question.Id);
        if (after is not null)
        {
            var at = after.AskedAt;
            var id = after.Id;
            rows = rows.Where(r => r.Question.AskedAt > at || (r.Question.AskedAt == at && r.Question.Id.CompareTo(id) > 0));
        }

        var page = await rows.Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > limit;

        return new QuestionPageRows(
            [.. (hasMore ? page[..limit] : page).Select(r => new QuestionRow(r.Question, r.IssueKey, r.Title))],
            total,
            hasMore);
    }

    public void Attach(IssueLabel attachment) => context.IssueLabels.Add(attachment);

    public Task DetachAsync(Guid issueId, Guid labelId, CancellationToken cancellationToken) =>
        context.IssueLabels.Where(il => il.IssueId == issueId && il.LabelId == labelId).ExecuteDeleteAsync(cancellationToken);

    public void Add(Blocker blocker) => context.Blockers.Add(blocker);

    public Task RemoveBlockerAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        context.Blockers.Where(b => b.BlockerId == blockerId && b.BlockedId == blockedId).ExecuteDeleteAsync(cancellationToken);

    public Task<bool> HasBlockerAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        context.Blockers.AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId, cancellationToken);

    // From the blocker backwards through what blocks it, a hundred steps at
    // most and never twice through one issue, until the blocked issue is
    // reached — the cycle — or the chain runs out.
    public async Task<IReadOnlyList<Guid>?> CycleThroughAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken)
    {
        var paths = await context.Database
            .SqlQueryRaw<string>(
                """
                with recursive chain (id, path) as (
                    select b.blocker_id, array[{0}, b.blocker_id]
                      from blocker b
                     where b.blocked_id = {0}
                    union all
                    select b.blocker_id, c.path || b.blocker_id
                      from blocker b
                      join chain c on b.blocked_id = c.id
                     where cardinality(c.path) <= {2}
                       and not (b.blocker_id = any (c.path))
                )
                select array_to_string(path, ',') as "Value"
                  from chain
                 where id = {1}
                 limit 1
                """,
                blockerId,
                blockedId,
                CycleDepth)
            .ToListAsync(cancellationToken);

        return paths.Count == 0 ? null : [.. paths[0].Split(',').Select(Guid.Parse)];
    }

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    // The epic joins in for its number alone, so that `sort=epic` can order by
    // the key a reader sees. It is a left join on a primary key beside the one
    // the project already needs.
    private IQueryable<IssueRow> Live() =>
        from i in context.IssueReads
        join p in context.Projects on i.ProjectId equals p.Id
        join e in context.Epics on i.EpicId equals e.Id into epics
        from e in epics.DefaultIfEmpty()
        where p.DeletedAt == null
        select new IssueRow
        {
            Id = i.Id, ProjectId = i.ProjectId, ProjectKey = p.Key, Number = i.Number, Title = i.Title,
            Description = i.Description, Result = i.Result, Status = i.Status, Ready = i.Ready, Priority = i.Priority,
            AssigneeId = i.AssigneeId, EpicId = i.EpicId, EpicNumber = e == null ? null : e.Number, ParentId = i.ParentId,
            ClaimedBy = i.ClaimedBy, ClaimedAt = i.ClaimedAt,
            ClaimExpiresAt = i.ClaimExpiresAt, AuthorId = i.AuthorId, CreatedAt = i.CreatedAt, UpdatedAt = i.UpdatedAt,
            ClosedAt = i.ClosedAt,
        };

    // The one read that sees deleted rows (ADR 0013): the table, not the view,
    // deliberately, in this one place.
    private IQueryable<IssueRow> Deleted() =>
        from i in context.Issues
        join p in context.Projects on i.ProjectId equals p.Id
        join e in context.Epics on i.EpicId equals e.Id into epics
        from e in epics.DefaultIfEmpty()
        where i.DeletedAt != null
        select new IssueRow
        {
            Id = i.Id, ProjectId = i.ProjectId, ProjectKey = p.Key, Number = i.Number, Title = i.Title,
            Description = i.Description, Result = i.Result, Status = i.Status, Ready = i.Ready, Priority = i.Priority,
            AssigneeId = i.AssigneeId, EpicId = i.EpicId, EpicNumber = e == null ? null : e.Number, ParentId = i.ParentId,
            ClaimedBy = i.Claim!.HolderId, ClaimedAt = i.Claim!.ClaimedAt,
            ClaimExpiresAt = i.Claim!.ExpiresAt, AuthorId = i.AuthorId, CreatedAt = i.CreatedAt, UpdatedAt = i.UpdatedAt,
            ClosedAt = i.ClosedAt, DeletedAt = i.DeletedAt, DeletedBy = i.DeletedBy,
        };

    private IQueryable<IssueRow> Filtered(IQueryable<IssueRow> rows, IssueQuery query)
    {
        var allowedProjectIds = query.AllowedProjectIds.ToArray();
        rows = rows.Where(r => allowedProjectIds.Contains(r.ProjectId));

        if (query.ProjectId is { } projectId)
        {
            rows = rows.Where(r => r.ProjectId == projectId);
        }

        if (query.Statuses.Count > 0)
        {
            var statuses = query.Statuses.ToArray();
            rows = rows.Where(r => statuses.Contains(r.Status));
        }

        if (query.Ready is { } ready)
        {
            rows = rows.Where(r => r.Ready == ready);
        }

        if (query.PriorityMin is { } min)
        {
            rows = rows.Where(r => r.Priority >= min);
        }

        if (query.PriorityMax is { } max)
        {
            rows = rows.Where(r => r.Priority <= max);
        }

        foreach (var name in query.LabelNames)
        {
            rows = rows.Where(r => context.IssueLabels.Any(il =>
                il.IssueId == r.Id && context.Labels.Any(l => l.Id == il.LabelId && l.Name == name && l.DeletedAt == null)));
        }

        if (query.EpicNone)
        {
            rows = rows.Where(r => r.EpicId == null);
        }
        else if (query.EpicId is { } epicId)
        {
            rows = rows.Where(r => r.EpicId == epicId);
        }

        if (query.AssigneeNone)
        {
            rows = rows.Where(r => r.AssigneeId == null);
        }
        else if (query.AssigneeId is { } assigneeId)
        {
            rows = rows.Where(r => r.AssigneeId == assigneeId);
        }

        if (query.ClaimedBy is { } holder)
        {
            rows = rows.Where(r => r.ClaimedBy == holder);
        }
        else if (query.ClaimedAtAll is { } claimed)
        {
            rows = claimed ? rows.Where(r => r.ClaimedBy != null) : rows.Where(r => r.ClaimedBy == null);
        }

        if (query.AuthorId is { } authorId)
        {
            rows = rows.Where(r => r.AuthorId == authorId);
        }

        if (query.Blocked is { } blocked)
        {
            var live = Live();
            rows = blocked
                ? rows.Where(r => context.Blockers.Any(b => b.BlockedId == r.Id && live.Any(f => f.Id == b.BlockerId && f.Status != IssueStatus.Done && f.Status != IssueStatus.Canceled)))
                : rows.Where(r => !context.Blockers.Any(b => b.BlockedId == r.Id && live.Any(f => f.Id == b.BlockerId && f.Status != IssueStatus.Done && f.Status != IssueStatus.Canceled)));
        }

        if (query.HasOpenQuestion is { } open)
        {
            rows = open
                ? rows.Where(r => context.Questions.Any(q => q.IssueId == r.Id && q.Answer == null))
                : rows.Where(r => !context.Questions.Any(q => q.IssueId == r.Id && q.Answer == null));
        }

        if (query.Search is { } search)
        {
            rows = rows.Where(r =>
                context.Issues.Any(i => i.Id == r.Id && EF.Property<NpgsqlTsVector>(i, "Search").Matches(EF.Functions.WebSearchToTsQuery("simple", search)))
                || context.Comments.Any(c => c.IssueId == r.Id && EF.Property<NpgsqlTsVector>(c, "Search").Matches(EF.Functions.WebSearchToTsQuery("simple", search)))
                || context.Questions.Any(q => q.IssueId == r.Id && EF.Property<NpgsqlTsVector>(q, "Search").Matches(EF.Functions.WebSearchToTsQuery("simple", search))));
        }

        return rows;
    }

    // `priority` sorts priority desc, created_at asc regardless of order — the
    // order `next` uses (docs/api.md, Pagination). The number and then the id
    // break every tie, so that a keyset cursor is exact and issues created in
    // one act come out in the order they were given.
    private static IQueryable<IssueRow> Sorted(IQueryable<IssueRow> rows, IssueSort sort, SortOrder order) =>
        (sort, order) switch
        {
            (IssueSort.Updated, SortOrder.Desc) => rows.OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Number).ThenByDescending(r => r.Id),
            (IssueSort.Updated, _) => rows.OrderBy(r => r.UpdatedAt).ThenBy(r => r.Number).ThenBy(r => r.Id),
            (IssueSort.Created, SortOrder.Desc) => rows.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Number).ThenByDescending(r => r.Id),
            (IssueSort.Created, _) => rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.Number).ThenBy(r => r.Id),
            (IssueSort.Priority, _) => rows.OrderByDescending(r => r.Priority).ThenBy(r => r.CreatedAt).ThenBy(r => r.Number).ThenBy(r => r.Id),
            // The epic key in the two halves it is made of, so that PLAN-E9
            // comes before PLAN-E10 rather than after it, and the issues under
            // no epic as one group at the end. Within a group, what is up
            // next: priority descending, then the number.
            (IssueSort.Epic, _) => rows
                .OrderBy(r => r.EpicNumber == null)
                .ThenBy(r => r.ProjectKey)
                .ThenBy(r => r.EpicNumber)
                .ThenByDescending(r => r.Priority)
                .ThenBy(r => r.Number)
                .ThenBy(r => r.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };

    private static IQueryable<IssueRow> After(IQueryable<IssueRow> rows, IssueSort sort, SortOrder order, IssuePosition after)
    {
        var time = after.Time ?? default;
        var number = after.Number;
        var id = after.Id;

        return (sort, order) switch
        {
            (IssueSort.Updated, SortOrder.Desc) => rows.Where(r =>
                r.UpdatedAt < time || (r.UpdatedAt == time && (r.Number < number || (r.Number == number && r.Id.CompareTo(id) < 0)))),
            (IssueSort.Updated, _) => rows.Where(r =>
                r.UpdatedAt > time || (r.UpdatedAt == time && (r.Number > number || (r.Number == number && r.Id.CompareTo(id) > 0)))),
            (IssueSort.Created, SortOrder.Desc) => rows.Where(r =>
                r.CreatedAt < time || (r.CreatedAt == time && (r.Number < number || (r.Number == number && r.Id.CompareTo(id) < 0)))),
            (IssueSort.Created, _) => rows.Where(r =>
                r.CreatedAt > time || (r.CreatedAt == time && (r.Number > number || (r.Number == number && r.Id.CompareTo(id) > 0)))),
            (IssueSort.Priority, _) => rows.Where(r =>
                r.Priority < (after.Priority ?? default)
                || (r.Priority == (after.Priority ?? default)
                    && (r.CreatedAt > time
                        || (r.CreatedAt == time && (r.Number > number || (r.Number == number && r.Id.CompareTo(id) > 0)))))),
            (IssueSort.Epic, _) => AfterEpic(rows, after),
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
    }

    /// <summary>
    /// The keyset for <c>sort=epic</c>, whose chain is six long: whether the
    /// row hangs under an epic at all, the project key, the epic number,
    /// priority descending, the number, the id. Whether the page ended inside
    /// an epic or in the group at the end is known before the query is built,
    /// so it is two statements rather than one with a case in it — this is the
    /// place where a wrong comparison silently skips or repeats rows.
    /// </summary>
    private static IQueryable<IssueRow> AfterEpic(IQueryable<IssueRow> rows, IssuePosition after)
    {
        var project = after.ProjectKey;
        var priority = after.Priority ?? default;
        var number = after.Number;
        var id = after.Id;

        if (after.EpicNumber is not { } epic)
        {
            // The page ended in the group at the end, which nothing follows
            // but itself.
            return rows.Where(r =>
                r.EpicNumber == null
                && (r.ProjectKey.CompareTo(project) > 0
                    || (r.ProjectKey == project
                        && (r.Priority < priority
                            || (r.Priority == priority
                                && (r.Number > number || (r.Number == number && r.Id.CompareTo(id) > 0)))))));
        }

        return rows.Where(r =>
            r.EpicNumber == null
            || r.ProjectKey.CompareTo(project) > 0
            || (r.ProjectKey == project
                && (r.EpicNumber > epic
                    || (r.EpicNumber == epic
                        && (r.Priority < priority
                            || (r.Priority == priority
                                && (r.Number > number || (r.Number == number && r.Id.CompareTo(id) > 0))))))));
    }
}
