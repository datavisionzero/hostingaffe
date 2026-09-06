using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

public sealed record ProgressShape(int Total, int Closed, int Done, int Canceled)
{
    public static ProgressShape Of(Progress progress) => new(progress.Total, progress.Closed, progress.Done, progress.Canceled);
}

/// <summary>The slim epic every list returns (<c>docs/api.md</c>).</summary>
public sealed record EpicSummaryShape(
    string Key,
    string Project,
    string Title,
    EpicStatus Status,
    IReadOnlyList<string> Labels,
    ProgressShape Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>The complete epic: the summary plus the living document, the author and the full labels.</summary>
public sealed record EpicShape(
    string Key,
    string Project,
    string Title,
    string Description,
    EpicStatus Status,
    IdentityRef Author,
    IReadOnlyList<LabelShape> Labels,
    ProgressShape Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);

public sealed record EpicPage(IReadOnlyList<EpicSummaryShape> Items, int Total, bool HasMore, string? NextCursor);

public sealed record CreateEpicRequest(string? Project, string? Title, string? Description, IReadOnlyList<string>? Labels);

/// <param name="DescriptionGiven">Present, even as <c>null</c>, which clears the document.</param>
public sealed record EpicChanges(string? Title, bool DescriptionGiven, string? Description, IReadOnlyList<string>? Labels);

public sealed record EpicListRequest(string? Project, string? Status, IReadOnlyList<string> Label, string? Cursor, int? Limit);

/// <summary>Turns epic rows into the two shapes, a page at a time.</summary>
public sealed class EpicAssembler(IEpics epics, IProjects projects, IIdentities identities)
{
    public async Task<IReadOnlyList<EpicSummaryShape>> SummariesAsync(IReadOnlyList<Epic> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(e => e.Id).ToArray();
        var labels = await epics.LabelsOfAsync(ids, cancellationToken);
        var progress = await epics.ProgressAsync(ids, cancellationToken);
        var keys = await ProjectKeysAsync(rows, cancellationToken);

        return
        [
            .. rows.Select(e => new EpicSummaryShape(
                EpicKey.Of(keys[e.ProjectId], e.Number),
                keys[e.ProjectId],
                e.Title,
                e.Status,
                [.. labels.Where(l => l.EpicId == e.Id).Select(l => l.Label.Name).Order(StringComparer.Ordinal)],
                ProgressShape.Of(progress[e.Id]),
                e.CreatedAt,
                e.UpdatedAt,
                e.ClosedAt)),
        ];
    }

    public async Task<EpicShape> CompleteAsync(Epic epic, CancellationToken cancellationToken)
    {
        var labels = await epics.LabelsOfAsync([epic.Id], cancellationToken);
        var progress = await epics.ProgressAsync([epic.Id], cancellationToken);
        var keys = await ProjectKeysAsync([epic], cancellationToken);
        var author = await identities.FindAsync(epic.CreatedBy, cancellationToken)
            ?? throw new InvalidOperationException($"Epic {epic.Id} has no author row.");

        return new EpicShape(
            EpicKey.Of(keys[epic.ProjectId], epic.Number),
            keys[epic.ProjectId],
            epic.Title,
            epic.Description,
            epic.Status,
            IdentityRef.Of(author),
            [.. labels.Select(l => LabelShape.Of(l.Label)).OrderBy(l => l.Name, StringComparer.Ordinal)],
            ProgressShape.Of(progress[epic.Id]),
            epic.CreatedAt,
            epic.UpdatedAt,
            epic.ClosedAt);
    }

    private async Task<Dictionary<Guid, string>> ProjectKeysAsync(IEnumerable<Epic> rows, CancellationToken cancellationToken)
    {
        var keys = new Dictionary<Guid, string>();
        foreach (var projectId in rows.Select(e => e.ProjectId).Distinct())
        {
            keys[projectId] = (await projects.FindByIdAsync(projectId, cancellationToken))?.Key
                ?? throw new InvalidOperationException($"Project {projectId} has no row.");
        }

        return keys;
    }
}

/// <summary>The lookups every epic act starts with.</summary>
public static class EpicLookup
{
    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public static async Task<(Epic Epic, Project Project)> LiveAsync(
        this IEpics epics, IProjects projects, ProjectScope scope, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        var (epic, project) = await epics.AnyAsync(projects, scope, key, settings, cancellationToken);

        return epic.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"Epic {key} is deleted and can be restored until at least {epic.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = epic.DeletedAt.Value + settings.DeletionGrace })
            : (epic, project);
    }

    public static async Task<(Epic Epic, Project Project)> AnyAsync(
        this IEpics epics, IProjects projects, ProjectScope scope, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        if (!EpicKey.TryParse(key, out var projectKey, out var number))
        {
            throw new Refusal(RefusalCode.NotFound, $"{key} is not an epic key.");
        }

        var project = await projects.LiveAsync(projectKey, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        var epic = await epics.FindAnyAsync(project.Id, number, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No epic {EpicKey.Of(projectKey, number)}.");

        return (epic, project);
    }
}

/// <summary>A theme several issues will hang under (VISION 7): the key from the project's epic counter, in one transaction with its labels.</summary>
public sealed class CreateEpic(
    ICallerIdentity callerIdentity,
    ProjectScope scope,
    IProjects projects,
    ILabels labels,
    IEpics epics,
    IHistory history,
    ITransactions transactions,
    EpicAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<EpicShape> ExecuteAsync(CreateEpicRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = callerIdentity.Caller;

        var project = await projects.LiveAsync(request.Project ?? throw Refusal.Validation("project", "A project key is required."), settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        var title = Validated.Field("title", () => Epic.NormalizeTitle(request.Title!));
        var resolved = await labels.ResolveLabelsAsync(project, request.Labels ?? [], "labels", cancellationToken);

        var epic = await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var number = await projects.AllocateEpicNumbersAsync(project.Id, 1, cancellationToken);
            var created = Epic.Create(project.Id, number, title, caller.Id, now);
            created.Describe(request.Description, now);

            epics.Add(created);
            history.Add(HistoryEntry.OnEpic(created.Id, caller.Id, now, HistoryField.Created));

            foreach (var label in resolved)
            {
                epics.Attach(EpicLabel.Attach(created.Id, label.Id));
                history.Add(HistoryEntry.OnEpic(created.Id, caller.Id, now, HistoryField.Label, newValue: label.Name));
            }

            await epics.SaveAsync(cancellationToken);
            return created;
        }, cancellationToken);

        return await assembler.CompleteAsync(epic, cancellationToken);
    }
}

/// <summary>A page of slim epics, newest first: `open` by default, `closed`, or `all`.</summary>
public sealed class ListEpics(IProjects projects, IEpics epics, EpicAssembler assembler, ProjectScope scope, InstanceSettings settings)
{
    public async Task<EpicPage> ExecuteAsync(EpicListRequest request, CancellationToken cancellationToken)
    {
        var limit = request.Limit ?? ListIssues.DefaultLimit;
        if (limit < 1 || limit > ListIssues.MaximumLimit)
        {
            throw Refusal.Validation("limit", $"limit is 1 to {ListIssues.MaximumLimit}.");
        }

        bool? closed = request.Status?.ToLowerInvariant() switch
        {
            null or "open" => false,
            "closed" => true,
            "all" => null,
            _ => throw Refusal.Validation("status", "status is open, closed or all."),
        };

        Guid? projectId = request.Project is null ? null : (await projects.LiveAsync(request.Project, settings, cancellationToken)).Id;
        if (projectId is { } selectedProjectId)
            await scope.RequireAsync(selectedProjectId, cancellationToken);
        var query = new EpicQuery(await scope.ProjectIdsAsync(cancellationToken), projectId, closed, request.Label);
        var after = request.Cursor is null ? null : EpicCursor.Decode(request.Cursor, query);

        var page = await epics.ListAsync(query, after, limit, cancellationToken);

        return new EpicPage(
            await assembler.SummariesAsync(page.Items, cancellationToken),
            page.Total,
            page.HasMore,
            page.HasMore ? EpicCursor.Encode(query, page.Items[^1]) : null);
    }
}

public sealed class ReadEpic(IProjects projects, ProjectScope scope, IEpics epics, EpicAssembler assembler, InstanceSettings settings)
{
    public async Task<EpicShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var (epic, _) = await epics.LiveAsync(projects, scope, key, settings, cancellationToken);
        return await assembler.CompleteAsync(epic, cancellationToken);
    }
}

/// <summary>
/// Title, the living document and the labels, guarded by <c>If-Match</c>
/// against <c>updated_at</c> — the guard several agents editing one description
/// need (VISION 7). The history records that the description changed, not how.
/// </summary>
public sealed class ChangeEpic(
    ICallerIdentity callerIdentity,
    IProjects projects,
    ProjectScope scope,
    ILabels labels,
    IEpics epics,
    IHistory history,
    ITransactions transactions,
    EpicAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<EpicShape> ExecuteAsync(string key, EpicChanges changes, string? ifMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var caller = callerIdentity.Caller;

        var (before, project) = await epics.LiveAsync(projects, scope, key, settings, cancellationToken);
        var expected = ChangeIssue.Expected(ifMatch);
        var newLabels = changes.Labels is null ? null : await labels.ResolveLabelsAsync(project, changes.Labels, "labels", cancellationToken);

        var epic = await transactions.RunAsync(async () =>
        {
            var row = await epics.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No epic {key}.");

            if (expected is { } version && row.UpdatedAt != version)
            {
                throw new Refusal(
                    RefusalCode.Stale,
                    $"{key} changed at {row.UpdatedAt:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}; you last read it at {version:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}.",
                    new Dictionary<string, object?> { ["current"] = await assembler.CompleteAsync(row, cancellationToken) });
            }

            var now = clock.GetUtcNow();

            if (changes.Title is not null && changes.Title != row.Title)
            {
                var old = row.Title;
                Validated.Field("title", () => { row.Retitle(changes.Title, now); return true; });
                history.Add(HistoryEntry.OnEpic(row.Id, caller.Id, now, HistoryField.Title, old, row.Title));
            }

            if (changes.DescriptionGiven && (changes.Description ?? string.Empty) != row.Description)
            {
                row.Describe(changes.Description, now);
                history.Add(HistoryEntry.OnEpic(row.Id, caller.Id, now, HistoryField.Description));
            }

            if (newLabels is not null)
            {
                var current = (await epics.LabelsOfAsync([row.Id], cancellationToken)).Select(l => l.Label).ToList();
                foreach (var gone in current.Where(c => newLabels.All(n => n.Id != c.Id)))
                {
                    await epics.DetachAsync(row.Id, gone.Id, cancellationToken);
                    history.Add(HistoryEntry.OnEpic(row.Id, caller.Id, now, HistoryField.Label, oldValue: gone.Name));
                    row.Touch(now);
                }

                foreach (var added in newLabels.Where(n => current.All(c => c.Id != n.Id)))
                {
                    epics.Attach(EpicLabel.Attach(row.Id, added.Id));
                    history.Add(HistoryEntry.OnEpic(row.Id, caller.Id, now, HistoryField.Label, newValue: added.Name));
                    row.Touch(now);
                }
            }

            await epics.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(epic, cancellationToken);
    }
}

/// <summary>Close, reopen, delete, restore: the four moves of a bracket, none of which gates an issue.</summary>
public sealed class MoveEpic(
    ICallerIdentity callerIdentity,
    IProjects projects,
    ProjectScope scope,
    IEpics epics,
    IHistory history,
    ITransactions transactions,
    EpicAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    /// <summary>Closed, whatever is still open; the response carries the progress so the CLI can list it.</summary>
    public Task<EpicShape> CloseAsync(string key, CancellationToken cancellationToken) =>
        OnLiveAsync(key, (epic, caller, now) =>
        {
            epic.Close(now);
            history.Add(HistoryEntry.OnEpic(epic.Id, caller.Id, now, HistoryField.Status, "open", "closed"));
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<EpicShape> ReopenAsync(string key, CancellationToken cancellationToken) =>
        OnLiveAsync(key, (epic, caller, now) =>
        {
            if (!epic.Closed)
            {
                throw new Refusal(RefusalCode.Transition, "The epic is open already.");
            }

            epic.Reopen(now);
            history.Add(HistoryEntry.OnEpic(epic.Id, caller.Id, now, HistoryField.Status, "closed", "open"));
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Soft, and refused with <c>has-issues</c> while any issue — deleted ones included — references it.</summary>
    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var (before, _) = await epics.LiveAsync(projects, scope, key, settings, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var epic = await epics.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No epic {key}.");

            var count = await epics.ReferencingIssuesAsync(epic.Id, cancellationToken);
            if (count > 0)
            {
                throw new Refusal(
                    RefusalCode.HasIssues,
                    $"{count} issue(s) reference {key}; move or delete them first, then the epic.",
                    new Dictionary<string, object?> { ["count"] = count });
            }

            var now = clock.GetUtcNow();
            epic.Delete(caller.Id, now);
            history.Add(HistoryEntry.OnEpic(epic.Id, caller.Id, now, HistoryField.Deleted));
            await epics.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<EpicShape> RestoreAsync(string key, CancellationToken cancellationToken)
    {
        var (before, _) = await epics.AnyAsync(projects, scope, key, settings, cancellationToken);
        if (!before.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"Epic {key} is not deleted.");
        }

        var epic = await transactions.RunAsync(async () =>
        {
            var row = await epics.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No epic {key}.");
            row.Restore();
            await epics.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(epic, cancellationToken);
    }

    private async Task<EpicShape> OnLiveAsync(string key, Func<Epic, Caller, DateTimeOffset, Task> move, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var (before, _) = await epics.LiveAsync(projects, scope, key, settings, cancellationToken);

        var epic = await transactions.RunAsync(async () =>
        {
            var row = await epics.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No epic {key}.");
            await move(row, caller, clock.GetUtcNow());
            await epics.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(epic, cancellationToken);
    }
}

internal static class EpicCursor
{
    private sealed record Payload(string F, DateTimeOffset T, int N, Guid I);

    public static string Encode(EpicQuery query, Epic last) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(Fingerprint(query), last.CreatedAt, last.Number, last.Id)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static EpicPosition Decode(string cursor, EpicQuery query)
    {
        Payload? payload;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(base64));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            payload = null;
        }

        return payload is null || payload.F != Fingerprint(query)
            ? throw new Refusal(RefusalCode.CursorInvalid, "The cursor is not one this server issued for these filters.")
            : new EpicPosition(payload.T, payload.N, payload.I);
    }

    private static string Fingerprint(EpicQuery query) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(query))))[..16];
}
