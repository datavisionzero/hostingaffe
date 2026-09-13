using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Reports;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// What a machine gets back for a report: the number it was given, and when the
/// instance received it. Deliberately narrow — a cron needs nothing else, and a
/// host should be handed nothing else (ADR 0016).
/// </summary>
public sealed record ReportReceiptShape(int Number, DateTimeOffset ReceivedAt);

/// <summary>
/// What a presented machine token admits: the machine it belongs to, or
/// nobody. The path is the one every other token takes — hash the secret, find
/// the row — and it ends at a machine rather than at an identity.
/// </summary>
/// <remarks>
/// A user token and an agent token admit nobody here, and nothing says whether
/// the secret was one: a report comes from the machine, and someone who could
/// post one by hand could forge a drift comparison with nothing showing
/// anywhere (ADR 0015, ADR 0016).
/// </remarks>
public sealed class AuthenticateMachineToken(IMachineTokens tokens)
{
    private const string Scheme = "Bearer";

    public async Task<MachineCaller?> ExecuteAsync(string? authorization, CancellationToken cancellationToken)
    {
        if (!TryReadSecret(authorization, out var secret))
        {
            return null;
        }

        var found = await tokens.FindByHashAsync(TokenSecret.HashOf(secret), cancellationToken);

        return found is null ? null : MachineCaller.Of(found.Value.Token, found.Value.Machine);
    }

    private static bool TryReadSecret(string? authorization, out string secret)
    {
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        var parts = authorization.Trim().Split(
            ' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 2
            || !parts[0].Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !TokenSecret.IsAcceptable(parts[1]))
        {
            return false;
        }

        secret = parts[1];
        return true;
    }
}

/// <summary>
/// A machine hands in a report. The one write of the whole feature, and the
/// only act a machine token reaches.
/// </summary>
/// <remarks>
/// <para>
/// What it does is check, number, store, and move the token's
/// <c>last_used_at</c>. What it does <strong>not</strong> do is set a field of
/// the machine, write a history row, record a deployment or touch an
/// installation — a report is a sample beside the record (ADR 0015).
/// </para>
/// <para>
/// The key in the path has to be the machine of the token, and the refusal does
/// not distinguish "there is no such machine" from "that one is not yours": a
/// token that could enumerate the record by trying keys would read something,
/// and this one reads nothing.
/// </para>
/// </remarks>
public sealed class HandInReport(
    ICallerMachine callerMachine,
    IMachineTokens tokens,
    IReports reports,
    ITransactions transactions,
    TimeProvider clock)
{
    /// <exception cref="Refusal"><c>not-found</c>, <c>unknown-field</c>, <c>validation</c> or <c>rate-limited</c>.</exception>
    public async Task<ReportReceiptShape> ExecuteAsync(
        string key, HandInReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var machine = callerMachine.Machine;

        if (!string.Equals(key?.Trim(), machine.MachineKey, StringComparison.Ordinal))
        {
            throw new Refusal(RefusalCode.NotFound, $"No machine {key?.Trim()}.");
        }

        var body = ReportWrites.Body(request);
        var now = clock.GetUtcNow();

        var collectedAt = request.CollectedAt
            ?? throw Refusal.Validation("collected_at", "The value is required.");

        return await transactions.RunAsync(async () =>
        {
            // A cron running amok, or a loop somebody wrote by hand. Read from
            // the rows rather than from memory, so that it holds however many
            // instances stand behind the address.
            if (await reports.LatestAsync(machine.MachineId, cancellationToken) is { } latest
                && now - latest.ReceivedAt < ReportWrites.MinimumBetweenReports)
            {
                var wait = (int)Math.Ceiling(
                    (ReportWrites.MinimumBetweenReports - (now - latest.ReceivedAt)).TotalSeconds);

                throw new Refusal(
                    RefusalCode.RateLimited,
                    $"{machine.MachineKey} reported less than a minute ago; one report a minute is what this takes.",
                    new Dictionary<string, object?> { ["retry_after"] = Math.Max(wait, 1) });
            }

            var number = await reports.NextNumberAsync(machine.MachineId, cancellationToken);

            var report = Validated.Field(
                "agent",
                () => Report.Record(machine.MachineId, number, collectedAt, now, request.Agent, body));

            reports.Add(report);

            // The one thing a report moves, and it is about the token rather
            // than about the machine (ADR 0016).
            if (await tokens.LiveForAsync(machine.MachineId, cancellationToken) is { } token)
            {
                token.Used(now);
            }

            await reports.SaveAsync(cancellationToken);

            return new ReportReceiptShape(report.Number, report.ReceivedAt);
        }, cancellationToken);
    }
}
