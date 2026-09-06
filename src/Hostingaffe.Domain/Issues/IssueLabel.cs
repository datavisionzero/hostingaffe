namespace Hostingaffe.Domain.Issues;

/// <summary>
/// A label on an issue. One row per attachment; the group-exclusivity of labels
/// is held by the write path that adds these rows, under the issue's lock
/// (<c>docs/storage.md</c>, Labels).
/// </summary>
public sealed class IssueLabel
{
    private IssueLabel()
    {
        // EF Core materializes through this; every other route goes through Attach.
    }

    private IssueLabel(Guid issueId, Guid labelId)
    {
        IssueId = issueId;
        LabelId = labelId;
    }

    public Guid IssueId { get; private init; }

    public Guid LabelId { get; private init; }

    public static IssueLabel Attach(Guid issueId, Guid labelId) => new(issueId, labelId);
}
