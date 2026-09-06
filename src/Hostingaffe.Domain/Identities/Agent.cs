namespace Hostingaffe.Domain.Identities;

/// <summary>
/// An AI identity that reaches the instance through the API under exactly one
/// agent token, and is owned by the user who created it (<c>CONTEXT.md</c>,
/// Agent).
/// </summary>
/// <remarks>
/// Never an administrator, whoever owns it (ADR 0015): there is no act here that
/// could make it one, and the table's check constraint refuses the row if
/// anything else tries. What an agent reports about itself — the metadata back
/// channel of VISION 12 — is a cut-two column.
/// </remarks>
public sealed class Agent : Identity
{
    public const int MetadataValueMaxLength = 100;

    private Agent()
    {
    }

    private Agent(Guid id, string name, Guid ownerId, DateTimeOffset createdAt)
        : base(id, name, administrator: false, createdAt)
    {
        OwnerId = ownerId;
    }

    /// <summary>
    /// The user who created this agent. Ownership answers <em>which</em> human
    /// is behind a token; it does not make the agent's acts that human's acts
    /// (ADR 0015).
    /// </summary>
    public Guid OwnerId { get; private init; }

    /// <summary>The last stable facts this agent reported about itself.</summary>
    public AgentMetadata? Metadata { get; private set; }

    public DateTimeOffset? MetadataReportedAt { get; private set; }

    public override IdentityKind Kind => IdentityKind.Agent;

    public static Agent Create(string name, Guid ownerId, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), NormalizeName(name), ownerId, createdAt);

    /// <summary>
    /// Applies one report. A field not present stays as it was; a present null
    /// clears it. The returned value is the immutable snapshot kept in history.
    /// </summary>
    public AgentMetadata ReportMetadata(
        bool kindGiven, string? kind,
        bool harnessGiven, string? harness,
        bool environmentGiven, string? environment,
        bool versionGiven, string? version,
        DateTimeOffset reportedAt)
    {
        var previous = Metadata ?? AgentMetadata.Empty;
        var reported = new AgentMetadata(
            kindGiven ? NormalizeMetadata("kind", kind) : previous.Kind,
            harnessGiven ? NormalizeMetadata("harness", harness) : previous.Harness,
            environmentGiven ? NormalizeMetadata("environment", environment) : previous.Environment,
            versionGiven ? NormalizeMetadata("version", version) : previous.Version);

        Metadata = reported;
        MetadataReportedAt = reportedAt;
        return reported;
    }

    private static string? NormalizeMetadata(string field, string? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= MetadataValueMaxLength
            ? value
            : throw new ArgumentException(
                $"Agent metadata {field} is at most {MetadataValueMaxLength} characters.", field);
    }
}
