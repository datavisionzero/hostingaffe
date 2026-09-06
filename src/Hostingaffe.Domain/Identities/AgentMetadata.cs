namespace Hostingaffe.Domain.Identities;

/// <summary>
/// Stable facts an agent reports about itself through its one-way back
/// channel (VISION 12). They describe the installation the agent is rather than
/// the run that reports them, so a later report from another run of the same
/// installation replaces them. Per-run facts belong on the issue, not here.
/// </summary>
public sealed record AgentMetadata(string? Kind, string? Harness, string? Environment, string? Version)
{
    public static AgentMetadata Empty { get; } = new(null, null, null, null);
}

/// <summary>One immutable report in the agent metadata history.</summary>
public sealed class AgentMetadataReport
{
    private AgentMetadataReport()
    {
    }

    private AgentMetadataReport(Guid id, Guid identityId, DateTimeOffset reportedAt, AgentMetadata metadata)
    {
        Id = id;
        IdentityId = identityId;
        ReportedAt = reportedAt;
        Metadata = metadata;
    }

    public Guid Id { get; private init; }
    public Guid IdentityId { get; private init; }
    public DateTimeOffset ReportedAt { get; private init; }
    public AgentMetadata Metadata { get; private init; } = null!;

    public static AgentMetadataReport Create(Guid identityId, DateTimeOffset reportedAt, AgentMetadata metadata) =>
        new(Guid.CreateVersion7(), identityId, reportedAt, metadata);
}
