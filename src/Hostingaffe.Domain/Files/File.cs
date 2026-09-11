using System.Text;
using Hostingaffe.Domain.History;

namespace Hostingaffe.Domain.Files;

/// <summary>
/// A UTF-8 text file a machine runs with, kept next to the thing it belongs to
/// (<c>CONTEXT.md</c>, File; VISION 7): a <c>compose.override.yml</c>, a Caddy
/// fragment, a systemd unit and its timer, a <c>bin/</c> script, an
/// <c>.envrc</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every write is a revision, and every earlier content stays.</strong>
/// What the file says now is the newest revision's content — derived, computed
/// on read, never a column a write has to remember to refresh. A write that
/// changes neither the content nor the mode bit makes no revision: a revision
/// that repeats its predecessor byte for byte is not a version of the file, and
/// <c>files sync</c> writing the whole set would otherwise number the history up
/// without saying anything.
/// </para>
/// <para>
/// The path is the address under the owner and does not change. Moving a file is
/// putting it at the new path and deleting the old one, which is also what
/// happens on the machine.
/// </para>
/// <para>
/// A machine's file also says which <em>directory</em> on the machine it lies
/// in, because a machine has no single one the way an installation has its path
/// (VISION 7, ADR 0008). That one <em>is</em> editable, and it makes no
/// revision: it says where the file lies, not what it says.
/// </para>
/// <para>
/// The name collides with <c>System.IO.File</c>, which is implicitly imported
/// everywhere. Every file that needs this one says so with an alias: the model's
/// word is <c>file</c> (<c>CONTEXT.md</c>), and a type named around the
/// collision would be a word the glossary does not have.
/// </para>
/// </remarks>
public sealed class File
{
    /// <summary>One megabyte of UTF-8, as VISION 7 caps it.</summary>
    public const int ContentMaxBytes = 1024 * 1024;

    private readonly List<FileRevision> _revisions = [];

    private File()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private File(Guid id, Anchor owner, string path, string? directory, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        MachineId = owner.Kind is AnchorKind.Machine ? owner.Id : null;
        InstallationId = owner.Kind is AnchorKind.Installation ? owner.Id : null;
        Path = path;
        Directory = directory;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>Set when the owner is a machine, and then the other one is not.</summary>
    public Guid? MachineId { get; private init; }

    /// <summary>Set when the owner is an installation, and then the other one is not.</summary>
    public Guid? InstallationId { get; private init; }

    /// <summary>Relative to the owner's directory, and unique under it.</summary>
    public string Path { get; private init; } = null!;

    /// <summary>
    /// Where the file lies on the machine, absolute — set when the owner is a
    /// machine, and nothing when it is an installation, whose own path is the
    /// one directory all of its files share (VISION 7, ADR 0008).
    /// </summary>
    public string? Directory { get; private set; }

    /// <summary>Every write, oldest first, each with the content it wrote.</summary>
    public IReadOnlyList<FileRevision> Revisions => _revisions;

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Deleted => DeletedAt is not null;

    /// <summary>The newest revision — what the file says now.</summary>
    public FileRevision Current =>
        _revisions.Count > 0
            ? _revisions[^1]
            : throw new InvalidOperationException("A file has at least one revision from the moment it exists.");

    /// <summary>The number of the newest revision, counted from one.</summary>
    public int Revision => Current.Number;

    /// <summary>What the file says now.</summary>
    public string Content => Current.Content;

    /// <summary>Whether it arrives runnable.</summary>
    public bool Executable => Current.Executable;

    /// <summary>
    /// How many bytes of UTF-8 the file is — derived from the newest revision's
    /// content like everything else about it, and counted the way
    /// <see cref="NormalizeContent"/> counts against <see cref="ContentMaxBytes"/>,
    /// so that what a list shows is the number a write is refused against.
    /// </summary>
    public int Size => Encoding.UTF8.GetByteCount(Content);

    /// <summary>Who wrote it last — the newest revision's author.</summary>
    public Guid UpdatedBy => Current.By;

    /// <summary>When it was last written; the version a guarded write is compared against.</summary>
    public DateTimeOffset UpdatedAt => Current.At;

    /// <summary>The revision by its number, or nothing where there is none.</summary>
    public FileRevision? At(int revision) => _revisions.SingleOrDefault(entry => entry.Number == revision);

    /// <exception cref="ArgumentException">
    /// The path is refused, the directory does not suit the owner, or the
    /// content is not text this holds.
    /// </exception>
    public static File Create(
        Anchor owner,
        string path,
        string? directory,
        string? content,
        bool executable,
        Guid createdBy,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var file = new File(
            Guid.CreateVersion7(),
            owner,
            FilePath.Normalize(path),
            DirectoryUnder(owner.Kind, directory),
            createdBy,
            createdAt);

        file._revisions.Add(new FileRevision(1, NormalizeContent(content), executable, createdBy, createdAt));

        return file;
    }

    /// <summary>
    /// The directory a file of this owner may carry: a machine's file says
    /// where on the machine it lies, and an installation's says nothing,
    /// because the installation's own path already said it once for all of
    /// them.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A machine's file has none, or an installation's file was given one.
    /// </exception>
    private static string? DirectoryUnder(AnchorKind kind, string? directory)
    {
        if (kind is AnchorKind.Installation)
        {
            return directory?.Trim() is { Length: > 0 }
                ? throw new ArgumentException(
                    "An installation's files lie under the installation's path; only a machine's file carries a directory of its own.",
                    "directory")
                : null;
        }

        return FileDirectory.Normalize(directory);
    }

    /// <summary>
    /// A new revision, unless the file already says exactly this. Answers what
    /// changed, in the words the API spells the fields with.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The content is not text this holds, or the directory does not suit the
    /// owner.
    /// </exception>
    public IReadOnlyList<FieldChange> Write(
        string? content, bool? executable, string? directory, Guid by, DateTimeOffset at)
    {
        var text = content is null ? Content : NormalizeContent(content);
        var bit = executable ?? Executable;
        var changes = new List<FieldChange>();

        if (text != Content || bit != Executable)
        {
            changes.Add(new FieldChange(
                "revision",
                Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (bit != Executable)
            {
                changes.Add(new FieldChange("executable", Executable ? "true" : "false", bit ? "true" : "false"));
            }

            _revisions.Add(new FileRevision(Revision + 1, text, bit, by, at));
        }

        // The directory is not part of a revision: it says where the file lies,
        // not what it says, and moving a unit from one root to another does not
        // make the unit a second version of itself.
        if (directory is not null)
        {
            var moved = DirectoryUnder(
                MachineId is not null ? AnchorKind.Machine : AnchorKind.Installation, directory);

            if (moved != Directory)
            {
                changes.Add(new FieldChange("directory", Directory, moved));
                Directory = moved;
            }
        }

        return changes;
    }

    /// <summary>Soft, with the grace period of everything else (ADR 0013).</summary>
    public void Delete(Guid by, DateTimeOffset at)
    {
        if (Deleted)
        {
            return;
        }

        DeletedAt = at;
        DeletedBy = by;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }

    /// <summary>
    /// UTF-8 text, at most a megabyte of it. What is not text is refused rather
    /// than replaced: VISION 7 rules binary content out, and a byte turned into
    /// a question mark would be a file that no longer runs.
    /// </summary>
    /// <exception cref="ArgumentException">It is over the cap, or it is not text.</exception>
    public static string NormalizeContent(string? content)
    {
        var text = content ?? string.Empty;

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A file is text; a null byte is not part of any.", nameof(content));
        }

        for (var at = 0; at < text.Length; at++)
        {
            // A lone half of a surrogate pair is a string that is not any
            // sequence of characters, and so not UTF-8 either.
            if (char.IsSurrogate(text[at])
                && !(char.IsHighSurrogate(text[at]) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1])))
            {
                throw new ArgumentException("A file is UTF-8 text, and this is not a sequence of characters.", nameof(content));
            }

            if (char.IsHighSurrogate(text[at]))
            {
                at++;
            }
        }

        return Encoding.UTF8.GetByteCount(text) > ContentMaxBytes
            ? throw new ArgumentException(
                $"A file holds at most {ContentMaxBytes} bytes of UTF-8; what is bigger belongs on the machine, not here.",
                nameof(content))
            : text;
    }
}
