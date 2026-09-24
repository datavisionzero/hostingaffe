using Hostingaffe.Domain.History;

namespace Hostingaffe.Domain.Providers;

/// <summary>The named source of external hosting for machines (ADR 0020).</summary>
public sealed class Provider
{
    public const int NameMaxLength = 200;

    private Provider() { }

    private Provider(Guid id, string key, string name, Guid by, DateTimeOffset at)
    {
        Id = id;
        Key = key;
        Name = name;
        Description = string.Empty;
        CreatedBy = by;
        CreatedAt = at;
        UpdatedBy = by;
        UpdatedAt = at;
    }

    public Guid Id { get; private init; }
    public string Key { get; private init; } = null!;
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    /// <summary>
    /// The composition the provider is recognised by, and the palette it is
    /// drawn in (ADR 0022). Both optional: a provider without them is shown with
    /// an emblem derived from its key, and that one is never stored.
    /// </summary>
    public Emblem? Emblem { get; private set; }

    public EmblemPalette? EmblemPalette { get; private set; }

    public Guid CreatedBy { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public Guid UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public bool Deleted => DeletedAt is not null;

    public static Provider Create(string key, string? name, Guid by, DateTimeOffset at)
    {
        var handle = Domain.Key.Normalize(key);
        return new Provider(
            Guid.CreateVersion7(), handle,
            string.IsNullOrWhiteSpace(name) ? handle : NormalizeName(name), by, at);
    }

    public IReadOnlyList<FieldChange> Apply(ProviderEdit edit, Guid by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var changes = new List<FieldChange>();
        Fields.Text("name", edit.Name, Name, value => Name = value ?? Key, NormalizeName, changes);
        Fields.Optional("emblem", edit.Emblem, Emblem, value => Emblem = value, changes);
        Fields.Optional("emblem_palette", edit.EmblemPalette, EmblemPalette, value => EmblemPalette = value, changes);
        if (edit.Description is not null && edit.Description != Description)
        {
            changes.Add(new FieldChange("description", null, null));
            Description = edit.Description;
        }

        if (changes.Count > 0)
        {
            UpdatedBy = by;
            UpdatedAt = at;
        }

        return changes;
    }

    public void Delete(Guid by, DateTimeOffset at)
    {
        if (Deleted) return;
        DeletedAt = at;
        DeletedBy = by;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }

    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? throw new ArgumentException("A provider has a name.", nameof(name))
            : Fields.Line(trimmed, NameMaxLength, "A provider name");
    }
}
