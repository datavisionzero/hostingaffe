using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// A machine's token as everyone but the machine sees it: that there is one,
/// which one, who issued it and when it was last used — and no secret anywhere,
/// because only the hash is kept (ADR 0016).
/// </summary>
/// <remarks>
/// Where a machine has no live token, this describes the last one it had, so
/// that "is this machine still reporting, and is that the token's doing" has an
/// answer. A machine that never had one answers with <c>present</c> false and
/// nothing else.
/// </remarks>
public sealed record MachineTokenShape(
    bool Present,
    string? Prefix,
    IdentityRef? IssuedBy,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? LastUsedAt,
    IdentityRef? RevokedBy,
    DateTimeOffset? RevokedAt);

/// <summary>
/// What issuing answers, and the one moment the secret exists outside the
/// host: it is shown once and never again, because the row keeps the hash.
/// </summary>
public sealed record IssuedMachineToken(string Machine, string Prefix, string Secret, DateTimeOffset IssuedAt);

/// <summary>Whether this machine has a token, and what became of the last one.</summary>
/// <remarks>
/// An agent may read this — there is no secret in it — and may neither issue
/// nor revoke: an agent administers no keys (ADR 0016, planaffe ADR 0015).
/// </remarks>
public sealed class ReadMachineToken(
    IMachines machines, IMachineTokens tokens, IIdentities identities, InstanceSettings settings)
{
    public async Task<MachineTokenShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var machine = await machines.LiveAsync(key, settings, cancellationToken);
        var token = await tokens.MostRecentAsync(machine.Id, cancellationToken);

        if (token is null)
        {
            return new MachineTokenShape(false, null, null, null, null, null, null);
        }

        var people = await identities.FindManyAsync(
            new[] { token.IssuedBy, token.RevokedBy }.OfType<Guid>().Distinct(), cancellationToken);

        return new MachineTokenShape(
            !token.Revoked,
            token.Prefix,
            IdentityRef.Of(people[token.IssuedBy]),
            token.IssuedAt,
            token.LastUsedAt,
            token.RevokedBy is { } revoker ? IdentityRef.Of(people[revoker]) : null,
            token.RevokedAt);
    }
}

/// <summary>
/// A token for a machine, issued by a person. The secret is in the answer and
/// nowhere else afterwards.
/// </summary>
/// <remarks>
/// A machine that already has one is refused unless the call says it is
/// rotating. Then the old token is revoked in the same move, and the cron on
/// the host fails visibly at its next run rather than quietly carrying on under
/// a key somebody meant to replace.
/// </remarks>
public sealed class IssueMachineToken(
    ICallerIdentity callerIdentity,
    IMachines machines,
    IMachineTokens tokens,
    IHistory history,
    ITransactions transactions,
    InstanceSettings settings,
    TimeProvider clock)
{
    /// <exception cref="Refusal"><c>forbidden</c>, <c>not-found</c> or <c>transition</c>.</exception>
    public async Task<IssuedMachineToken> ExecuteAsync(
        string key, bool rotate, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("issue a machine's token");
        var said = Validated.Note(note);

        var machine = await machines.LiveAsync(key, settings, cancellationToken);

        return await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var live = await tokens.LiveForAsync(machine.Id, cancellationToken);

            if (live is not null && !rotate)
            {
                throw new Refusal(
                    RefusalCode.Transition,
                    $"{machine.Key} already has a token, issued {live.IssuedAt:u}. Rotate to replace it; the old one is then revoked and the host stops reporting until it carries the new one.");
            }

            if (live is not null)
            {
                live.Revoke(caller.Id, now);
                history.Add(HistoryEntry.OnMachine(
                    machine.Id, caller.Id, now, HistoryField.Token, live.Prefix, null, said ?? "rotated"));
            }

            var secret = TokenSecret.Generate();
            var issued = MachineToken.Issue(machine.Id, secret, caller.Id, now);
            tokens.Add(issued);

            // The one thing about a report that belongs in the history: a person
            // changed what the machine may do (ADR 0015).
            history.Add(HistoryEntry.OnMachine(
                machine.Id, caller.Id, now, HistoryField.Token, null, issued.Prefix, said));

            await tokens.SaveAsync(cancellationToken);

            return new IssuedMachineToken(machine.Key, issued.Prefix, secret, issued.IssuedAt);
        }, cancellationToken);
    }
}

/// <summary>A machine's token, taken back by a person. It stays as a row.</summary>
public sealed class RevokeMachineToken(
    ICallerIdentity callerIdentity,
    IMachines machines,
    IMachineTokens tokens,
    IHistory history,
    ITransactions transactions,
    InstanceSettings settings,
    TimeProvider clock)
{
    /// <exception cref="Refusal"><c>forbidden</c> or <c>not-found</c>.</exception>
    public async Task ExecuteAsync(string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("revoke a machine's token");
        var said = Validated.Note(note);

        var machine = await machines.LiveAsync(key, settings, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var live = await tokens.LiveForAsync(machine.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"{machine.Key} has no token to revoke.");

            var now = clock.GetUtcNow();
            live.Revoke(caller.Id, now);

            history.Add(HistoryEntry.OnMachine(
                machine.Id, caller.Id, now, HistoryField.Token, live.Prefix, null, said));

            await tokens.SaveAsync(cancellationToken);

            return true;
        }, cancellationToken);
    }
}
