namespace Hostingaffe.Domain.Releases;

public sealed class ReleaseIssue
{
    private ReleaseIssue() { }
    private ReleaseIssue(Guid releaseId, Guid issueId) { ReleaseId = releaseId; IssueId = issueId; }
    public Guid ReleaseId { get; private init; }
    public Guid IssueId { get; private init; }
    public static ReleaseIssue Attach(Guid releaseId, Guid issueId) => new(releaseId, issueId);
}
