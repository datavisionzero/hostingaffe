namespace Hostingaffe.Domain.Providers;

/// <summary>The editable fields of a provider; null leaves a field alone.</summary>
/// <remarks>
/// <see cref="Emblem"/> and <see cref="EmblemPalette"/> are closed sets that can
/// be cleared, so they arrive as the words they are spelled with: the empty
/// string clears them the way it clears a text field (ADR 0022).
/// </remarks>
public sealed record ProviderEdit(
    string? Name = null, string? Description = null, string? Emblem = null, string? EmblemPalette = null);
