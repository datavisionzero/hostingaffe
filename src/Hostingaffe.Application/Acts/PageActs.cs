using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Pages;

namespace Hostingaffe.Application.Acts;

/// <summary>The slim page every list returns: everything but the document itself (ADR 0012).</summary>
public sealed record PageSummaryShape(
    string Slug,
    string Title,
    IdentityRef UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The complete page: the summary plus the Markdown and the author.</summary>
public sealed record PageShape(
    string Slug,
    string Title,
    string Body,
    IdentityRef Author,
    IdentityRef UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatePageRequest(string? Slug, string? Title, string? Body);

/// <param name="BodyGiven">Present, even as <c>null</c>, which empties the document.</param>
public sealed record PageChanges(string? Slug, string? Title, bool BodyGiven, string? Body);

/// <summary>Turns page rows into the two shapes, resolving the identities once for the whole list.</summary>
public sealed class PageAssembler(IIdentities identities)
{
    public async Task<IReadOnlyList<PageSummaryShape>> SummariesAsync(
        IReadOnlyList<Page> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var people = await PeopleAsync(rows.Select(p => p.UpdatedBy), cancellationToken);

        return
        [
            .. rows.Select(p => new PageSummaryShape(
                p.Slug,
                p.Title,
                people[p.UpdatedBy],
                p.CreatedAt,
                p.UpdatedAt)),
        ];
    }

    public async Task<PageShape> CompleteAsync(Page page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var people = await PeopleAsync([page.CreatedBy, page.UpdatedBy], cancellationToken);

        return new PageShape(
            page.Slug,
            page.Title,
            page.Body,
            people[page.CreatedBy],
            people[page.UpdatedBy],
            page.CreatedAt,
            page.UpdatedAt);
    }

    private async Task<Dictionary<Guid, IdentityRef>> PeopleAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var people = new Dictionary<Guid, IdentityRef>();
        foreach (var id in ids.Distinct())
        {
            people[id] = IdentityRef.Of(
                await identities.FindAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"Identity {id} has no row."));
        }

        return people;
    }
}

/// <summary>
/// One entry of the history: who, when, which field, from what to what
/// (<c>CONTEXT.md</c>, History). The values are the ones the instance wrote —
/// a title as it read, a slug as it read, and nothing at all where the field
/// records that a text changed rather than how.
/// </summary>
public sealed record HistoryEntryShape(
    long Id,
    IdentityRef Actor,
    DateTimeOffset At,
    string Field,
    string? OldValue,
    string? NewValue,
    string? Note);

/// <summary>The lookup every page act starts with: the slug.</summary>
public static class PageLookup
{
    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public static async Task<Page> LiveAsync(
        this IPages pages, string slug, InstanceSettings settings, CancellationToken cancellationToken)
    {
        var page = await pages.AnyAsync(slug, cancellationToken);

        return page.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"Page {page.Slug} is deleted and can be restored until at least {page.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = page.DeletedAt.Value + settings.DeletionGrace })
            : page;
    }

    public static async Task<Page> AnyAsync(this IPages pages, string slug, CancellationToken cancellationToken)
    {
        // An address that is not a slug names nothing, and says so as `not-found`
        // rather than as `validation`: it arrived in the path, not in a body.
        var normalized = slug?.Trim() ?? string.Empty;

        return (Slug.IsValid(normalized) ? await pages.FindAnyAsync(normalized, cancellationToken) : null)
            ?? throw new Refusal(RefusalCode.NotFound, $"No page {normalized}.");
    }
}

/// <summary>
/// Every page, by slug, without the bodies. Not paginated: the wiki is flat and
/// small, and <c>q</c> is what a reader navigates it by, since the search is
/// what the product put in a hierarchy's place (VISION 7).
/// </summary>
public sealed class ListPages(IPages pages, PageAssembler assembler)
{
    public async Task<IReadOnlyList<PageSummaryShape>> ExecuteAsync(string? search, CancellationToken cancellationToken) =>
        await assembler.SummariesAsync(await pages.ListAsync(search, cancellationToken), cancellationToken);
}

public sealed class ReadPage(IPages pages, PageAssembler assembler, InstanceSettings settings)
{
    public async Task<PageShape> ExecuteAsync(string slug, CancellationToken cancellationToken) =>
        await assembler.CompleteAsync(await pages.LiveAsync(slug, settings, cancellationToken), cancellationToken);
}

/// <summary>
/// The history of a page: who, when, which field, from what to what, oldest
/// first. Not paginated — a page's history is as long as its edits, and a wiki
/// page is edited by hand.
/// </summary>
public sealed class ReadPageHistory(
    IPages pages, IIdentities identities, IHistory history, InstanceSettings settings)
{
    public async Task<IReadOnlyList<HistoryEntryShape>> ExecuteAsync(string slug, CancellationToken cancellationToken)
    {
        var page = await pages.LiveAsync(slug, settings, cancellationToken);
        var entries = await history.ListAsync(page.Id, cancellationToken);

        var people = await identities.FindManyAsync(
            entries.Select(entry => entry.ActorId).Distinct(), cancellationToken);

        return
        [
            .. entries.Select(entry => new HistoryEntryShape(
                entry.Id,
                IdentityRef.Of(people[entry.ActorId]),
                entry.At,
                entry.Field,
                entry.OldValue,
                entry.NewValue,
                entry.Note)),
        ];
    }
}

/// <summary>A page of the wiki, in one transaction.</summary>
public sealed class CreatePage(
    ICallerIdentity callerIdentity,
    IPages pages,
    IHistory history,
    ITransactions transactions,
    PageAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<PageShape> ExecuteAsync(CreatePageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = callerIdentity.Caller;

        var slug = Validated.Field("slug", () => Slug.Normalize(request.Slug ?? string.Empty));
        var title = Validated.Field("title", () => Page.NormalizeTitle(request.Title!));

        await PageWrites.TakenAsync(pages, slug, settings, cancellationToken);

        var page = await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var created = Page.Create(slug, title, request.Body, caller.Id, now);

            pages.Add(created);
            history.Add(HistoryEntry.OnPage(created.Id, caller.Id, now, HistoryField.Created));

            await pages.SaveAsync(cancellationToken);
            return created;
        }, cancellationToken);

        return await assembler.CompleteAsync(page, cancellationToken);
    }
}

/// <summary>
/// Title, the document and the address, guarded by <c>If-Match</c> against
/// <c>updated_at</c> — the guard a text a human and an agent both edit needs.
/// The history records that the body changed, not how; a rename carries both
/// addresses, because nothing else keeps the old one.
/// </summary>
public sealed class ChangePage(
    ICallerIdentity callerIdentity,
    IPages pages,
    IHistory history,
    ITransactions transactions,
    PageAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<PageShape> ExecuteAsync(
        string slug, PageChanges changes, string? ifMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var caller = callerIdentity.Caller;

        var before = await pages.LiveAsync(slug, settings, cancellationToken);
        var expected = GuardedWrite.Expected(ifMatch);

        var renamed = changes.Slug is null ? null : Validated.Field("slug", () => Slug.Normalize(changes.Slug));
        if (renamed is not null && renamed != before.Slug)
        {
            await PageWrites.TakenAsync(pages, renamed, settings, cancellationToken);
        }

        var page = await transactions.RunAsync(async () =>
        {
            var row = await pages.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No page {slug}.");

            if (expected is { } version && row.UpdatedAt != version)
            {
                throw new Refusal(
                    RefusalCode.Stale,
                    $"{row.Slug} changed at {row.UpdatedAt:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}; you last read it at {version:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}.",
                    new Dictionary<string, object?> { ["current"] = await assembler.CompleteAsync(row, cancellationToken) });
            }

            var now = clock.GetUtcNow();

            if (renamed is not null && renamed != row.Slug)
            {
                var old = row.Slug;
                row.Rename(renamed, caller.Id, now);
                history.Add(HistoryEntry.OnPage(row.Id, caller.Id, now, HistoryField.Slug, old, row.Slug));
            }

            if (changes.Title is not null && changes.Title != row.Title)
            {
                var old = row.Title;
                Validated.Field("title", () => { row.Retitle(changes.Title, caller.Id, now); return true; });
                history.Add(HistoryEntry.OnPage(row.Id, caller.Id, now, HistoryField.Title, old, row.Title));
            }

            if (changes.BodyGiven && (changes.Body ?? string.Empty) != row.Body)
            {
                row.Rewrite(changes.Body, caller.Id, now);
                history.Add(HistoryEntry.OnPage(row.Id, caller.Id, now, HistoryField.Body));
            }

            await pages.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(page, cancellationToken);
    }
}

/// <summary>Delete and restore: soft, with the grace period of everything else (ADR 0013).</summary>
public sealed class MovePage(
    ICallerIdentity callerIdentity,
    IPages pages,
    IHistory history,
    ITransactions transactions,
    PageAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task DeleteAsync(string slug, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var before = await pages.LiveAsync(slug, settings, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var row = await pages.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No page {slug}.");

            var now = clock.GetUtcNow();
            row.Delete(caller.Id, now);
            history.Add(HistoryEntry.OnPage(row.Id, caller.Id, now, HistoryField.Deleted));
            await pages.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    /// <summary>The slug was never given away while the page was deleted, so this cannot land on a taken name.</summary>
    public async Task<PageShape> RestoreAsync(string slug, CancellationToken cancellationToken)
    {
        var before = await pages.AnyAsync(slug, cancellationToken);
        if (!before.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"Page {before.Slug} is not deleted.");
        }

        var page = await transactions.RunAsync(async () =>
        {
            var row = await pages.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No page {slug}.");
            row.Restore();
            await pages.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(page, cancellationToken);
    }
}

internal static class PageWrites
{
    /// <summary>
    /// A slug already taken is refused as <c>validation</c>, and a deleted
    /// page's slug says so rather than pretending the name is free.
    /// </summary>
    public static async Task TakenAsync(
        IPages pages, string slug, InstanceSettings settings, CancellationToken cancellationToken)
    {
        if (await pages.FindAnyAsync(slug, cancellationToken) is not { } existing)
        {
            return;
        }

        throw Refusal.Validation("slug", existing.Deleted
            ? $"The page {slug} is deleted and can be restored until at least {existing.DeletedAt!.Value + settings.DeletionGrace:u}; a new one cannot take its slug until it is purged."
            : $"The page {slug} exists.");
    }
}
