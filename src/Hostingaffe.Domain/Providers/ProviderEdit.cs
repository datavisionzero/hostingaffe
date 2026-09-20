namespace Hostingaffe.Domain.Providers;

/// <summary>The editable fields of a provider; null leaves a field alone.</summary>
public sealed record ProviderEdit(string? Name = null, string? Description = null);
