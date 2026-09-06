namespace Hostingaffe.Domain.Epics;

/// <summary>
/// A label on an epic. One row per attachment, and nothing else on it: what a
/// label means is on the label, and whether it is visible is the label's
/// <c>DeletedAt</c>, not this row's.
/// </summary>
public sealed class EpicLabel
{
    private EpicLabel()
    {
        // EF Core materializes through this; every other route goes through Attach.
    }

    private EpicLabel(Guid epicId, Guid labelId)
    {
        EpicId = epicId;
        LabelId = labelId;
    }

    public Guid EpicId { get; private init; }

    public Guid LabelId { get; private init; }

    public static EpicLabel Attach(Guid epicId, Guid labelId) => new(epicId, labelId);
}
