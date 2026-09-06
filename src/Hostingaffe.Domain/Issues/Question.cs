namespace Hostingaffe.Domain.Issues;

/// <summary>
/// An open state on an issue: what somebody needs to know before the work can
/// go on, with an answer that closes it (<c>CONTEXT.md</c>, Question). Open
/// while <see cref="Answer"/> is null.
/// </summary>
/// <remarks>
/// The three answer columns come and go together, and the table's check
/// constraint says so as well. Asking does not release the claim; whoever does
/// not wait releases (VISION 10).
/// </remarks>
public sealed class Question
{
    private Question()
    {
        // EF Core materializes through this; every other route goes through Ask.
    }

    private Question(Guid id, Guid projectId, Guid issueId, string text, Guid askedBy, DateTimeOffset askedAt)
    {
        Id = id;
        ProjectId = projectId;
        IssueId = issueId;
        Text = text;
        AskedBy = askedBy;
        AskedAt = askedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>Denormalised from the issue for the project's wake-up channel.</summary>
    public Guid ProjectId { get; private init; }

    public Guid IssueId { get; private init; }

    /// <summary>What the asker needs to know, as Markdown.</summary>
    public string Text { get; private init; } = null!;

    public Guid AskedBy { get; private init; }

    public DateTimeOffset AskedAt { get; private init; }

    public string? Answer { get; private set; }

    public Guid? AnsweredBy { get; private set; }

    public DateTimeOffset? AnsweredAt { get; private set; }

    public bool Open => Answer is null;

    /// <exception cref="ArgumentException"><paramref name="text"/> is blank.</exception>
    public static Question Ask(Guid projectId, Guid issueId, string text, Guid askedBy, DateTimeOffset askedAt) =>
        string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("Whoever is stuck has to say on what.", nameof(text))
            : new(Guid.CreateVersion7(), projectId, issueId, text.Trim(), askedBy, askedAt);

    /// <exception cref="ArgumentException"><paramref name="answer"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">The question is already answered.</exception>
    public void AnswerWith(string answer, Guid answeredBy, DateTimeOffset answeredAt)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new ArgumentException("An answer says something.", nameof(answer));
        }

        if (!Open)
        {
            throw new InvalidOperationException("The question is already answered.");
        }

        Answer = answer.Trim();
        AnsweredBy = answeredBy;
        AnsweredAt = answeredAt;
    }
}
