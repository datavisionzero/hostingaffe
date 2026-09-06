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

    /// <summary>A page's Markdown. Recorded without values: <em>that</em> the text changed, not how.</summary>
    public const string Body = "body";

    /// <summary>A page's address, renamed: the old slug and the new one (ADR 0021).</summary>
    public const string Slug = "slug";

    public const string Deleted = "deleted";
}
