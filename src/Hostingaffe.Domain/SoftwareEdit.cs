namespace Hostingaffe.Domain;

/// <summary>
/// What a caller wants a software to say. Every field is optional;
/// <c>null</c> leaves the field alone, and the empty string clears a text field
/// (<c>docs/api.md</c>, Software).
/// </summary>
/// <remarks>
/// There is no version here, and there will not be one: a software carries no
/// version, because versions belong to deployments (VISION 7).
/// </remarks>
public sealed record SoftwareEdit
{
    public string? Name { get; init; }

    public string? Homepage { get; init; }

    public string? Repository { get; init; }

    public string? Image { get; init; }

    public string? Description { get; init; }
}
