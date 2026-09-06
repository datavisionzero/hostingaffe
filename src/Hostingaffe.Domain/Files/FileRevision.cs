namespace Hostingaffe.Domain.Files;

/// <summary>
/// One write of a file, kept whole (<c>CONTEXT.md</c>, File). Files are the one
/// place the history keeps <em>content</em> rather than the fact of a change,
/// and the reason is practical: rolling back a Compose file needs the previous
/// Compose file.
/// </summary>
/// <remarks>
/// The mode bit is part of the revision and not only of the file, so that going
/// back to a revision brings back the file as it was — a script that was
/// runnable stays runnable.
/// </remarks>
public sealed class FileRevision
{
    private FileRevision()
    {
        // EF Core materializes through this; every other route goes through the file.
    }

    internal FileRevision(int number, string content, bool executable, Guid by, DateTimeOffset at)
    {
        Number = number;
        Content = content;
        Executable = executable;
        By = by;
        At = at;
    }

    /// <summary>Counted from one, upward, per file.</summary>
    public int Number { get; private init; }

    /// <summary>UTF-8 text, capped at one megabyte.</summary>
    public string Content { get; private init; } = null!;

    /// <summary>The only mode bit there is, so that a script arrives runnable.</summary>
    public bool Executable { get; private init; }

    public Guid By { get; private init; }

    public DateTimeOffset At { get; private init; }
}
