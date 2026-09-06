namespace Hostingaffe.Domain.History;

/// <summary>
/// What a history entry says changed, spelled the way the API spells the field
/// (<c>docs/storage.md</c>, The history).
/// </summary>
public static class HistoryField
{
    /// <summary>The row's birth, with no values.</summary>
    public const string Created = "created";

    public const string Title = "title";

    /// <summary>Recorded without values: <em>that</em> the text changed, not how.</summary>
    public const string Description = "description";

    /// <inheritdoc cref="Description"/>
    public const string Result = "result";

    /// <summary>A page's Markdown. <inheritdoc cref="Description"/></summary>
    public const string Body = "body";

    /// <summary>A page's address, renamed: the old slug and the new one (ADR 0021).</summary>
    public const string Slug = "slug";

    public const string Status = "status";

    public const string Ready = "ready";

    public const string Priority = "priority";

    public const string Assignee = "assignee";

    public const string Claim = "claim";

    public const string Epic = "epic";
    public const string Parent = "parent";

    /// <summary>An edge: an addition carries the new value, a removal the old.</summary>
    public const string Label = "label";

    /// <inheritdoc cref="Label"/>
    public const string BlockedBy = "blocked_by";

    /// <summary>The release an issue is recorded in; empty where it is in none.</summary>
    public const string Release = "release";

    public const string Deleted = "deleted";

    /// <summary>
    /// A comment its author corrected, or somebody took away (ADR 0022). The
    /// entry names which comment and which of the two it was, never the text:
    /// a history that kept what was withdrawn would keep exactly the thing the
    /// withdrawal was for.
    /// </summary>
    public const string Comment = "comment";
}

/// <summary>
/// The two things a value cannot carry, written into an entry's note.
/// </summary>
public static class HistoryNote
{
    /// <summary>
    /// On the claim entry of a successor whose predecessor's claim had lapsed —
    /// the one trace an expiry leaves, written by whoever comes next (VISION 11).
    /// </summary>
    public const string Expired = "expired";

    /// <summary>On a claim taken with <c>--force</c>.</summary>
    public const string Forced = "forced";

    /// <summary>On the <c>comment</c> entry of a comment its author rewrote.</summary>
    public const string Edited = "edited";

    /// <summary>On the <c>comment</c> entry of a comment somebody took away.</summary>
    public const string Withdrawn = "withdrawn";
}
