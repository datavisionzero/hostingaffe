namespace Hostingaffe.Domain.History;

/// <summary>
/// What a history row is about (<c>CONTEXT.md</c>, History). The history is one
/// table whose rows name their subject rather than one table per thing that has
/// a history, because the mechanism is the same for all of them and a second
/// copy would be the one that drifts.
/// </summary>
public enum HistorySubject
{
    Page,
    Machine,
    Software,
}
