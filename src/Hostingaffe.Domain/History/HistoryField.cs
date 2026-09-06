namespace Hostingaffe.Domain.History;

/// <summary>
/// What a history entry says changed, spelled the way the API spells the field
/// (<c>docs/storage.md</c>, The history).
/// </summary>
/// <remarks>
/// What is here are the names that belong to no one field of one type —
/// a row's birth and its deletion — and the page's, whose fields have no other
/// place that lists them. A machine's are the field names its own change
/// produces, so that the two cannot drift apart.
/// </remarks>
public static class HistoryField
{
    /// <summary>The row's birth, with no values.</summary>
    public const string Created = "created";

    public const string Title = "title";

    /// <summary>A page's Markdown. Recorded without values: <em>that</em> the text changed, not how.</summary>
    public const string Body = "body";

    /// <summary>A page's address, renamed: the old slug and the new one (ADR 0021).</summary>
    public const string Slug = "slug";

    public const string Deleted = "deleted";
}
