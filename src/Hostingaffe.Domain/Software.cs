using System.Text.RegularExpressions;
using Hostingaffe.Domain.History;

namespace Hostingaffe.Domain;

/// <summary>
/// What an installation is an installation of — <c>caddy</c>, <c>postgres</c>,
/// <c>logaffe</c> (<c>CONTEXT.md</c>, Software; VISION 7). It exists once per
/// instance, so that "where is this running, and in which versions?" has a
/// place where it can be asked.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It carries no version.</strong> Versions belong to deployments, and
/// a version field here would be the most comfortable way to lose them: there
/// would be two truths, and nobody would keep the second one.
/// </para>
/// <para>
/// The word is uncountable in English, which is why this type lives at the root
/// of the Domain rather than in a folder of its own: a namespace named
/// <c>Software</c> beside a type named <c>Software</c> is an ambiguity every
/// reference then has to spell around. The collection is
/// <c>/api/software</c>, never <c>/api/softwares</c>, and the plural is
/// circumscribed wherever it is needed.
/// </para>
/// </remarks>
public sealed partial class Software
{
    public const int NameMaxLength = 200;

    /// <summary>What a URL and an image name fit in.</summary>
    public const int ReferenceMaxLength = 500;

    /// <summary>
    /// One component of a container image name: lower case, with single
    /// separators between the parts, as the registries spell them.
    /// </summary>
    private const string ImageComponentPattern = "^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*$";

    /// <summary>A registry, which is the one component that may carry a dot and a port.</summary>
    private const string ImageRegistryPattern = "^[a-z0-9]+(?:[.-][a-z0-9]+)*(?::[0-9]+)?$";

    private Software()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Software(Guid id, string key, string name, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        Key = key;
        Name = name;
        Description = string.Empty;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = createdBy;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>The handle, chosen once and never changed (<c>CONTEXT.md</c>, Key).</summary>
    public string Key { get; private init; } = null!;

    /// <summary>The display name; the key where the caller gave none.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Where the thing lives on the web.</summary>
    public string? Homepage { get; private set; }

    /// <summary>The upstream source.</summary>
    public string? Repository { get; private set; }

    /// <summary>The container image name <em>without a tag</em>: the tag belongs to a deployment.</summary>
    public string? Image { get; private set; }

    /// <summary>Markdown: what it is, and how this instance uses it in general.</summary>
    public string Description { get; private set; } = null!;

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>The version a guarded write is compared against.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Deleted => DeletedAt is not null;

    /// <exception cref="ArgumentException">The key or the name does not hold.</exception>
    public static Software Create(string key, string? name, Guid createdBy, DateTimeOffset createdAt)
    {
        var handle = Domain.Key.Normalize(key);

        return new Software(
            Guid.CreateVersion7(),
            handle,
            string.IsNullOrWhiteSpace(name) ? handle : NormalizeName(name),
            createdBy,
            createdAt);
    }

    /// <summary>
    /// Applies what the caller gave and answers with what actually changed, in
    /// the words the API spells the fields with.
    /// </summary>
    /// <exception cref="ArgumentException">A value does not hold; the message names the field.</exception>
    public IReadOnlyList<FieldChange> Apply(SoftwareEdit edit, Guid by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var changes = new List<FieldChange>();

        Fields.Text("name", edit.Name, Name, value => Name = value ?? Key, NormalizeName, changes);
        Fields.Text("homepage", edit.Homepage, Homepage, value => Homepage = value, value => Url(value, "homepage"), changes);
        Fields.Text("repository", edit.Repository, Repository, value => Repository = value, value => Url(value, "repository"), changes);
        Fields.Text("image", edit.Image, Image, value => Image = value, NormalizeImage, changes);

        // A description records that it changed, never how: the history is read
        // for what happened, and the text itself is one read away.
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

    /// <summary>Soft, with the grace period of everything else (ADR 0013).</summary>
    public void Delete(Guid by, DateTimeOffset at)
    {
        if (Deleted)
        {
            return;
        }

        DeletedAt = at;
        DeletedBy = by;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }

    /// <exception cref="ArgumentException"><paramref name="name"/> is blank, spans lines, or is too long.</exception>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? throw new ArgumentException("A software has a name.", nameof(name))
            : Fields.Line(trimmed, NameMaxLength, "A software name");
    }

    /// <summary>
    /// The name of a container image, without a tag and without a digest:
    /// <c>caddy</c>, <c>ghcr.io/datavisionzero/logaffe</c>. A <c>:tag</c> is
    /// refused rather than dropped, because a caller who wrote one meant it —
    /// and it belongs to the deployment, which is the thing that has a version.
    /// </summary>
    /// <exception cref="ArgumentException">It is not an image name, or it carries a tag or a digest.</exception>
    public static string NormalizeImage(string image)
    {
        var trimmed = image?.Trim() ?? string.Empty;
        Fields.Line(trimmed, ReferenceMaxLength, "An image");

        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An image is the name of a container image without a digest; what was deployed is a deployment's ref.");
        }

        var components = trimmed.Split('/');

        // A registry is the first component and only where there is another one
        // after it: `caddy` is an image, not a host, and `caddy:2` is a tag on
        // an image rather than a port on a registry.
        var first = components.Length > 1 && Registry().IsMatch(components[0])
            && (components[0].Contains('.', StringComparison.Ordinal)
                || components[0].Contains(':', StringComparison.Ordinal)
                || components[0] == "localhost")
            ? 1
            : 0;

        for (var at = first; at < components.Length; at++)
        {
            if (components[at].Contains(':', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "An image is the name of a container image without a tag; the tag belongs to the deployment.");
            }

            if (!Component().IsMatch(components[at]))
            {
                throw new ArgumentException(
                    "An image is a container image name: lower case, in parts separated by slashes.");
            }
        }

        return trimmed;
    }

    /// <summary>
    /// An absolute <c>http</c> or <c>https</c> address. Nothing else is a
    /// homepage or a repository, and a half-typed one is refused where it was
    /// typed rather than found broken by whoever clicks it.
    /// </summary>
    /// <exception cref="ArgumentException">It is not an absolute http(s) URL.</exception>
    public static string Url(string value, string field)
    {
        Fields.Line(value ?? string.Empty, ReferenceMaxLength, $"A {field}");

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(parsed.Host)
            ? value!
            : throw new ArgumentException($"A {field} is an http or https address.");
    }

    [GeneratedRegex(ImageComponentPattern)]
    private static partial Regex Component();

    [GeneratedRegex(ImageRegistryPattern)]
    private static partial Regex Registry();
}
