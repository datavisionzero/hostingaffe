namespace Hostingaffe.Domain.Issues;

/// <summary>
/// A note on an issue that forces nobody to act (<c>CONTEXT.md</c>, Comment).
/// Its author may correct it and take it back
/// (<see href="../../../docs/adr/0022-a-comment-can-be-corrected-and-withdrawn-by-its-author.md">ADR 0022</see>);
/// a correction is visible, because a text that changes quietly is what the
/// history exists against.
/// </summary>
public sealed class Comment
{
    private Comment()
    {
        // EF Core materializes through this; every other route goes through Write.
    }

    private Comment(Guid id, Guid issueId, Guid authorId, string body, DateTimeOffset createdAt)
    {
        Id = id;
        IssueId = issueId;
        AuthorId = authorId;
        Body = body;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    public Guid IssueId { get; private init; }

    public Guid AuthorId { get; private init; }

    public string Body { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>When it was last corrected, or nothing while it is as written.</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <exception cref="ArgumentException"><paramref name="body"/> is blank.</exception>
    public static Comment Write(Guid issueId, Guid authorId, string body, DateTimeOffset createdAt) =>
        string.IsNullOrWhiteSpace(body)
            ? throw new ArgumentException("A comment says something.", nameof(body))
            : new(Guid.CreateVersion7(), issueId, authorId, body.Trim(), createdAt);

    /// <summary>
    /// Rewrite what was said. Emptying it is not an edit — a comment that says
    /// nothing is one that should have been taken back instead.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="body"/> is blank.</exception>
    public void Rewrite(string body, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("A comment says something; an empty one is withdrawn, not saved.", nameof(body));
        }

        Body = body.Trim();
        EditedAt = at;
    }
}
