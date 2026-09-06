namespace Hostingaffe.Domain.Issues;

public enum ClaimOutcomeKind
{
    /// <summary>Nobody held it.</summary>
    Taken,

    /// <summary>The caller held it already; the claim is extended.</summary>
    Extended,

    /// <summary>The previous claim had lapsed — the one trace an expiry leaves, written by the successor.</summary>
    TakenAfterExpiry,

    /// <summary>Taken over somebody's unexpired claim with <c>force</c>.</summary>
    Forced,
}

/// <summary>What a claim did, and whom it displaced, for the history entry.</summary>
public sealed record ClaimOutcome(ClaimOutcomeKind Kind, Guid? PreviousHolder);
