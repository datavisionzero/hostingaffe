using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// A user creates an agent: identity and token in one transaction, the name
/// assigned when omitted, the secret shown once (VISION 12, ADR 0015).
/// </summary>
public sealed class CreateAgent(ICallerIdentity callerIdentity, IIdentities identities, TimeProvider clock)
{
    /// <summary>
    /// How often an assigned name may collide before that is a bug rather than
    /// bad luck: the space is ninety thousand names.
    /// </summary>
    private const int AssignmentAttempts = 10;

    public async Task<CreatedAgent> ExecuteAsync(string? name, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("create an agent");

        var normalized = string.IsNullOrWhiteSpace(name)
            ? await AssignedAsync(cancellationToken)
            : Validated.Field("name", () => Identity.NormalizeName(name));

        if (await identities.NameTakenAsync(normalized, cancellationToken))
        {
            throw Refusal.Validation("name", $"The name {normalized} is taken; names are unique across users and agents, whatever the case.");
        }

        var now = clock.GetUtcNow();
        var agent = Agent.Create(normalized, caller.Id, now);
        var secret = TokenSecret.Generate();
        var token = Token.Issue(agent, secret, now);

        await identities.AddAsync(agent, token, cancellationToken);

        return new CreatedAgent(
            agent.Id, agent.Kind, agent.Name,
            new IdentityRef(caller.Id, caller.Kind, caller.Name),
            agent.CreatedAt,
            new IssuedToken(token.Id, token.Prefix, secret, token.CreatedAt));
    }

    private async Task<string> AssignedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < AssignmentAttempts; attempt++)
        {
            var candidate = AgentName.Assign();
            if (!await identities.NameTakenAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free agent name in {AssignmentAttempts} attempts.");
    }
}

/// <summary>Every agent with its owner and its token, revoked ones included: the identity stays (ADR 0013).</summary>
public sealed class ListAgents(ICallerIdentity callerIdentity, IIdentities identities)
{
    public async Task<IReadOnlyList<AgentSummary>> ExecuteAsync(CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireUser("list agents");

        return
        [
            .. (await identities.ListAgentsAsync(cancellationToken)).Select(row => new AgentSummary(
                row.Agent.Id, row.Agent.Kind, row.Agent.Name,
                IdentityRef.Of(row.Owner), row.Agent.CreatedAt, TokenSummary.Of(row.Token),
                row.Agent.Metadata, row.Agent.MetadataReportedAt)),
        ];
    }
}

/// <summary>
/// The owner or an administrator renames an agent. The history keeps the id, so
/// old entries show the new name.
/// </summary>
public sealed class RenameAgent(ICallerIdentity callerIdentity, IIdentities identities)
{
    public async Task<AgentSummary> ExecuteAsync(string? address, string? name, CancellationToken cancellationToken)
    {
        var agent = await Owned.AgentAsync(callerIdentity.Caller, identities, address, "rename an agent", cancellationToken);

        var normalized = Validated.Field("name", () => Identity.NormalizeName(name!));
        if (!normalized.Equals(agent.Name, StringComparison.OrdinalIgnoreCase)
            && await identities.NameTakenAsync(normalized, cancellationToken))
        {
            throw Refusal.Validation("name", $"The name {normalized} is taken; names are unique across users and agents, whatever the case.");
        }

        agent.Rename(normalized);
        await identities.RecordRenameAsync(agent, cancellationToken);

        var row = (await identities.ListAgentsAsync(cancellationToken)).Single(r => r.Agent.Id == agent.Id);

        return new AgentSummary(
            row.Agent.Id, row.Agent.Kind, row.Agent.Name,
            IdentityRef.Of(row.Owner), row.Agent.CreatedAt, TokenSummary.Of(row.Token),
            row.Agent.Metadata, row.Agent.MetadataReportedAt);
    }
}

public sealed record AgentMetadataChanges(
    bool KindGiven, string? Kind,
    bool HarnessGiven, string? Harness,
    bool EnvironmentGiven, string? Environment,
    bool VersionGiven, string? Version);

/// <summary>An agent reports stable facts about itself; nobody writes them on its behalf.</summary>
public sealed class ReportAgentMetadata(ICallerIdentity callerIdentity, IIdentities identities, TimeProvider clock)
{
    public async Task<Me> ExecuteAsync(AgentMetadataChanges changes, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        if (!caller.IsAgent)
        {
            throw new Refusal(RefusalCode.Forbidden, "A user has no agent metadata to report.");
        }

        var agent = await identities.FindAgentAsync(caller.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Authenticated agent {caller.Id} does not exist.");

        var now = clock.GetUtcNow();
        AgentMetadata reported;
        try
        {
            reported = agent.ReportMetadata(
                changes.KindGiven, changes.Kind,
                changes.HarnessGiven, changes.Harness,
                changes.EnvironmentGiven, changes.Environment,
                changes.VersionGiven, changes.Version,
                now);
        }
        catch (ArgumentException invalid)
        {
            throw Refusal.Validation(invalid.ParamName ?? "metadata", invalid.Message);
        }

        await identities.RecordMetadataAsync(agent, AgentMetadataReport.Create(agent.Id, now, reported), cancellationToken);

        var owner = await identities.FindAsync(agent.OwnerId, cancellationToken)
            ?? throw new InvalidOperationException($"Agent {agent.Id} has no owner; the schema does not allow that.");
        return new Me(agent.Id, agent.Kind, agent.Name, false, null, IdentityRef.Of(owner),
            new TokenRef(caller.TokenPrefix, caller.TokenCreatedAt), agent.Metadata, agent.MetadataReportedAt);
    }
}

/// <summary>
/// The owner or an administrator revokes an agent: its one token stops
/// authenticating from the next request on, and the identity stays, naming the
/// agent in everything it ever did (ADR 0013).
/// </summary>
public sealed class RevokeAgent(ICallerIdentity callerIdentity, IIdentities identities, ITokens tokens, TimeProvider clock)
{
    public async Task ExecuteAsync(string? address, CancellationToken cancellationToken)
    {
        var agent = await Owned.AgentAsync(callerIdentity.Caller, identities, address, "revoke an agent", cancellationToken);

        var token = await tokens.FindAgentTokenAsync(agent.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Agent {agent.Id} has no token; the schema does not allow that.");

        if (token.Revoked)
        {
            return;
        }

        token.Revoke(clock.GetUtcNow());
        await tokens.RecordRevocationAsync(token, cancellationToken);
    }
}

/// <summary>
/// The line the two acts above share: the address names an agent that exists,
/// and the caller is its owner or an administrator. The address is an id or a
/// name (<see cref="IdentityAddress"/>); a name belonging to a user misses the
/// same way an unknown id does, because neither is an agent.
/// </summary>
internal static class Owned
{
    public static async Task<Agent> AgentAsync(
        Caller caller, IIdentities identities, string? address, string act, CancellationToken cancellationToken)
    {
        caller.RequireUser(act);

        var id = await IdentityAddress.ResolveAsync(address, identities, cancellationToken);
        var agent = id is null ? null : await identities.FindAgentAsync(id.Value, cancellationToken);
        if (agent is null)
        {
            throw new Refusal(RefusalCode.NotFound, $"No agent {address}.");
        }

        return caller.Administrator || agent.OwnerId == caller.Id
            ? agent
            : throw new Refusal(RefusalCode.Forbidden, $"Only the owner of an agent or an administrator may {act}.");
    }
}
