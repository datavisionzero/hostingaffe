namespace Hostingaffe.Domain.Epics;

/// <summary>
/// The two states of a bracket: open, or closed. Closing an epic gates nothing —
/// its issues stay workable (VISION 7).
/// </summary>
public enum EpicStatus
{
    Open,
    Closed,
}
