namespace Hostingaffe.Domain.Pages;

/// <summary>
/// A label on a page. One row per attachment, and nothing else on it: what a
/// label means is on the label, and whether it is visible is the label's
/// <c>DeletedAt</c>, not this row's.
/// </summary>
public sealed class PageLabel
{
    private PageLabel()
    {
        // EF Core materializes through this; every other route goes through Attach.
    }

    private PageLabel(Guid pageId, Guid labelId)
    {
        PageId = pageId;
        LabelId = labelId;
    }

    public Guid PageId { get; private init; }

    public Guid LabelId { get; private init; }

    public static PageLabel Attach(Guid pageId, Guid labelId) => new(pageId, labelId);
}
