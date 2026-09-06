namespace Hostingaffe.Domain.Issues;

/// <summary>
/// The fixed scale of VISION 8: monotonically increasing, so that ordering by
/// it descending needs no special case. The numbers are the API's and the
/// CLI's, which is why they are spelled here.
/// </summary>
public enum Priority : short
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}
