namespace Hostingaffe.Domain.Providers;

/// <summary>
/// The geometric composition a provider is recognised by (<c>CONTEXT.md</c>,
/// Provider; ADR 0022). Closed: every value is a drawing the web application
/// ships with, and nothing is uploaded. An emblem is not a machine's
/// <see cref="Machines.Avatar"/>, and the two sets share no word.
/// </summary>
public enum Emblem
{
    Orbit,
    Arch,
    Peak,
    Split,
    Quarter,
    Stack,
    Wave,
    Grid,
    Target,
    Bloom,
    Eclipse,
    Chevron,
    Bridge,
    Tiles,
    Beam,
    Steps,
}
