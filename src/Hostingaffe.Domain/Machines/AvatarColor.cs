namespace Hostingaffe.Domain.Machines;

/// <summary>
/// The colour a machine's <see cref="Avatar"/> is drawn in (ADR 0021). Closed
/// rather than free, because every drawing is legible in exactly these, in the
/// light and the dark theme alike.
/// </summary>
public enum AvatarColor
{
    Brown,
    Slate,
    Teal,
    Orange,
    Berry,
    Sage,
    Blue,
    Red,
    Mustard,
    Lavender,
}
