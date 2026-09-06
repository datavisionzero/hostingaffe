using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Files;
using Hostingaffe.Domain.History;

using File = Hostingaffe.Domain.Files.File;
using FilePath = Hostingaffe.Domain.Files.FilePath;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// The slim file every list returns: where it is, what it is, and how often it
/// has been written. The content is what would make it expensive, and the
/// content is not in it (ADR 0012).
/// </summary>
public sealed record FileSummaryShape(
    AnchorShape Owner,
    string Path,
    bool Executable,
    int Revision,
    IdentityRef UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A file at one revision — the newest by default, or the one asked for. The
/// shape is the same either way, because "the file as it was" is the file.
/// </summary>
public sealed record FileShape(
    AnchorShape Owner,
    string Path,
    bool Executable,
    string Content,
    int Revision,
    IdentityRef CreatedBy,
    IdentityRef UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One write, without what it wrote: the list of them is read to choose one.</summary>
public sealed record FileRevisionShape(
    int Revision,
    bool Executable,
    IdentityRef By,
    DateTimeOffset At);

/// <summary>What a caller sends to put a file under an owner.</summary>
public sealed record CreateFileRequest(string? Path, string? Content, bool? Executable)
{
    /// <inheritdoc cref="WriteFileRequest.UnknownFields"/>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// What a caller sends to write one: the content, the mode bit, or both. A
/// field left out stays as it is, and a write that changes neither makes no
/// revision.
/// </summary>
public sealed record WriteFileRequest(string? Content, bool? Executable)
{
    /// <summary>
    /// Whatever the caller sent that this object does not define. A closed
    /// request object refuses it as <c>unknown-field</c> rather than ignoring
    /// what somebody meant (<c>docs/api.md</c>, Conventions) — which is what
    /// refuses a <c>path</c> here: a file does not move, it is put somewhere
    /// else and the old one deleted.
    /// </summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>Turns file rows into the three shapes, resolving identities once for the whole list.</summary>
public sealed class FileAssembler(IIdentities identities)
{
    public async Task<IReadOnlyList<FileSummaryShape>> SummariesAsync(
        Anchor owner, IReadOnlyList<File> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var people = await identities.FindManyAsync(
            rows.Select(row => row.UpdatedBy).Distinct(), cancellationToken);

        return
        [
            .. rows.Select(row => new FileSummaryShape(
                AnchorShape.Of(owner),
                row.Path,
                row.Executable,
                row.Revision,
                IdentityRef.Of(people[row.UpdatedBy]),
                row.UpdatedAt)),
        ];
    }

    public async Task<FileShape> CompleteAsync(
        Anchor owner, File file, FileRevision revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(revision);

        var people = await identities.FindManyAsync([file.CreatedBy, revision.By], cancellationToken);

        return new FileShape(
            AnchorShape.Of(owner),
            file.Path,
            revision.Executable,
            revision.Content,
            revision.Number,
            IdentityRef.Of(people[file.CreatedBy]),
            IdentityRef.Of(people[revision.By]),
            file.CreatedAt,
            revision.At);
    }

    public async Task<IReadOnlyList<FileRevisionShape>> RevisionsAsync(
        File file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        var people = await identities.FindManyAsync(
            file.Revisions.Select(revision => revision.By).Distinct(), cancellationToken);

        return
        [
            .. file.Revisions
                .OrderByDescending(revision => revision.Number)
                .Select(revision => new FileRevisionShape(
                    revision.Number,
                    revision.Executable,
                    IdentityRef.Of(people[revision.By]),
                    revision.At)),
        ];
    }
}

/// <summary>
/// The two lookups every file act starts with: which owner, and which file
/// under it.
/// </summary>
public sealed class FileLookup(IMachines machines, IInstallations installations, IFiles files, InstanceSettings settings)
{
    /// <summary>
    /// The owner an address named. A machine and an installation are looked up
    /// the way they always are, so a deleted one says <c>deleted</c> and an
    /// unknown one says <c>not-found</c>.
    /// </summary>
    public async Task<Anchor> OwnerAsync(AnchorKind kind, string key, CancellationToken cancellationToken)
    {
        var id = kind is AnchorKind.Machine
            ? (await machines.LiveAsync(key, settings, cancellationToken)).Id
            : (await installations.LiveAsync(key, settings, cancellationToken)).Id;

        return new Anchor(kind, id, key.Trim());
    }

    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public async Task<File> LiveAsync(Anchor owner, string path, CancellationToken cancellationToken)
    {
        var file = await AnyAsync(owner, path, cancellationToken);

        return file.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"The file {file.Path} of {owner} is deleted and can be restored until at least {file.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = file.DeletedAt.Value + settings.DeletionGrace })
            : file;
    }

    public async Task<File> AnyAsync(Anchor owner, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // An address that is not a path this product keeps names nothing, and
        // says so as `not-found` rather than as `validation`: it arrived in the
        // path of the request, where nobody sent a field.
        var normalized = path?.Trim() ?? string.Empty;

        return (FilePath.IsValid(normalized) ? await files.FindAnyAsync(owner, normalized, cancellationToken) : null)
            ?? throw new Refusal(RefusalCode.NotFound, $"No file {normalized} of {owner}.");
    }
}

/// <summary>Every live file of one owner, by path, without the contents.</summary>
public sealed class ListFiles(IFiles files, FileLookup lookup, FileAssembler assembler)
{
    public async Task<IReadOnlyList<FileSummaryShape>> ExecuteAsync(
        AnchorKind kind, string key, CancellationToken cancellationToken)
    {
        var owner = await lookup.OwnerAsync(kind, key, cancellationToken);
        return await assembler.SummariesAsync(owner, await files.ListAsync(owner, cancellationToken), cancellationToken);
    }
}

/// <summary>
/// One file, at its newest revision or at the one asked for. A revision that
/// never existed is <c>not-found</c>: it is part of the address.
/// </summary>
public sealed class ReadFile(FileLookup lookup, FileAssembler assembler)
{
    public async Task<FileShape> ExecuteAsync(
        AnchorKind kind, string key, string path, int? revision, CancellationToken cancellationToken)
    {
        var owner = await lookup.OwnerAsync(kind, key, cancellationToken);
        var file = await lookup.LiveAsync(owner, path, cancellationToken);

        var wanted = revision is { } number
            ? file.At(number) ?? throw new Refusal(
                RefusalCode.NotFound, $"The file {file.Path} of {owner} has no revision {number}; it has {file.Revision}.")
            : file.Current;

        return await assembler.CompleteAsync(owner, file, wanted, cancellationToken);
    }
}

/// <summary>Every write of one file, newest first, without what each of them wrote.</summary>
public sealed class ReadFileRevisions(FileLookup lookup, FileAssembler assembler)
{
    public async Task<IReadOnlyList<FileRevisionShape>> ExecuteAsync(
        AnchorKind kind, string key, string path, CancellationToken cancellationToken)
    {
        var owner = await lookup.OwnerAsync(kind, key, cancellationToken);
        return await assembler.RevisionsAsync(
            await lookup.LiveAsync(owner, path, cancellationToken), cancellationToken);
    }
}

/// <summary>The history of a file: who, when, which field, from what to what, oldest first.</summary>
public sealed class ReadFileHistory(FileLookup lookup, IIdentities identities, IHistory history)
{
    public async Task<IReadOnlyList<HistoryEntryShape>> ExecuteAsync(
        AnchorKind kind, string key, string path, CancellationToken cancellationToken)
    {
        var owner = await lookup.OwnerAsync(kind, key, cancellationToken);
        var file = await lookup.LiveAsync(owner, path, cancellationToken);
        var entries = await history.ListAsync(HistorySubject.File, file.Id, cancellationToken);

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

/// <summary>A file, with its first revision, in one transaction.</summary>
public sealed class CreateFile(
    ICallerIdentity callerIdentity,
    IFiles files,
    FileLookup lookup,
    IHistory history,
    ITransactions transactions,
    FileAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<FileShape> ExecuteAsync(
        AnchorKind kind, string key, CreateFileRequest request, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FileWrites.Closed(request.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);

        var owner = await lookup.OwnerAsync(kind, key, cancellationToken);
        var path = Validated.Field("path", () => FilePath.Normalize(request.Path ?? string.Empty));

        await FileWrites.TakenAsync(files, owner, path, settings, cancellationToken);

        var file = await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var created = Validated.Field(
                "content",
                () => File.Create(owner, path, request.Content, request.Executable ?? false, caller.Id, now));

            files.Add(created);
            history.Add(HistoryEntry.OnFile(created.Id, caller.Id, now, HistoryField.Created, null, path, said));

            await files.SaveAsync(cancellationToken);
            return created;
        }, cancellationToken);

        return await assembler.CompleteAsync(owner, file, file.Current, cancellationToken);
    }
}

/// <summary>
/// A new revision, unless the file already says exactly this. Guarded by
/// <c>If-Match</c> against the <b>revision</b>: the file is the one record
/// whose history keeps content, so what it hands a reader to hand back is the
/// number of the write, not the moment of it (<c>docs/api.md</c>, Guarding a
/// write).
/// </summary>
public sealed class WriteFile(
    ICallerIdentity callerIdentity,
    IFiles files,
    FileLookup lookup,
    IHistory history,
    ITransactions transactions,
    FileAssembler assembler,
    TimeProvider clock)
{
    public async Task<FileShape> ExecuteAsync(
        AnchorKind kind,
        string key,
        string path,
        WriteFileRequest request,
        string? ifMatch,
        string? note,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FileWrites.Closed(request.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);

        var owner = await lookup.OwnerAsync(kind, key, cancellationToken);
        var before = await lookup.LiveAsync(owner, path, cancellationToken);
        var expected = GuardedWrite.ExpectedRevision(ifMatch);

        var file = await transactions.RunAsync(async () =>
        {
            var row = await files.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No file {path} of {owner}.");

            if (expected is { } revision && row.Revision != revision)
            {
                throw new Refusal(
                    RefusalCode.Stale,
                    $"{row.Path} is at revision {row.Revision}; you last read revision {revision}.",
                    new Dictionary<string, object?>
                    {
                        ["current"] = await assembler.CompleteAsync(owner, row, row.Current, cancellationToken),
                    });
            }

            var now = clock.GetUtcNow();

            foreach (var change in Validated.Field(
                "content", () => row.Write(request.Content, request.Executable, caller.Id, now)))
            {
                history.Add(HistoryEntry.OnFile(
                    row.Id, caller.Id, now, change.Field, change.OldValue, change.NewValue, said));
            }

            await files.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(owner, file, file.Current, cancellationToken);
    }
}

internal static class FileWrites
{
    /// <summary>
    /// A field the object does not define is <c>unknown-field</c>, never
    /// silently ignored (<c>docs/api.md</c>, Conventions).
    /// </summary>
    public static void Closed(Dictionary<string, JsonElement>? unknown)
    {
        if (unknown?.Keys.FirstOrDefault() is { } field)
        {
            throw new Refusal(
                RefusalCode.UnknownField,
                field switch
                {
                    "path" => "A file's path is its address and does not change; put it at the new path and delete the old one.",
                    "revision" => "A revision is made by writing, never given: it is the count of the writes.",
                    "owner" => "A file's owner is the address it was written to, not a field of it.",
                    _ => $"A file has no field {field}.",
                },
                new Dictionary<string, object?> { ["field"] = field });
        }
    }

    /// <summary>
    /// A path already taken under this owner is refused as <c>validation</c>,
    /// and a deleted file's path says so rather than pretending it is free.
    /// </summary>
    public static async Task TakenAsync(
        IFiles files, Anchor owner, string path, InstanceSettings settings, CancellationToken cancellationToken)
    {
        if (await files.FindAnyAsync(owner, path, cancellationToken) is not { } existing)
        {
            return;
        }

        throw Refusal.Validation("path", existing.Deleted
            ? $"The file {path} of {owner} is deleted and can be restored until at least {existing.DeletedAt!.Value + settings.DeletionGrace:u}; a new one cannot take its path until it is purged."
            : $"The file {path} of {owner} exists; write it instead.");
    }
}
