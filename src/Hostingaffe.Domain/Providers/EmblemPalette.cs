namespace Hostingaffe.Domain.Providers;

/// <summary>
/// The three colours a provider's <see cref="Emblem"/> is drawn in, the first
/// of them the tile it stands on (ADR 0022). Closed rather than free, because
/// every composition is legible in exactly these, whichever theme it lands in.
/// </summary>
public enum EmblemPalette
{
    Bauhaus,
    Ember,
    Meadow,
    Lagoon,
    Dusk,
    Citrus,
    Orchid,
    Granite,
    Coral,
    Glacier,
}
