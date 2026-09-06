using System.Text.RegularExpressions;

namespace Hostingaffe.Domain;

/// <summary>
/// The handle an operator chooses for an entity: short, lower case, and
/// immutable once given (<c>CONTEXT.md</c>, Key; VISION 7).
/// </summary>
/// <remarks>
/// <para>
/// A key is unique <em>per entity type across the instance</em>, not per
/// parent: there is one machine <c>caddy</c>, and naming it needs no parent. A
/// machine <c>caddy</c> and a software <c>caddy</c> may coexist, and usually
/// will, because every call names the type before the key.
/// </para>
/// <para>
/// Uniqueness is the store's and the database's, not this type's — it needs the
/// other rows. What is here is the shape, and it is the same shape a page's
/// slug has, for the same reason: a key is read aloud in running text and
/// appears in an address, where an underscore or a capital reads as punctuation
/// nobody meant.
/// </para>
/// <para>
/// That a key is never reused, not even after the purge, is a rule of deleting
/// and lives with it.
/// </para>
/// </remarks>
public static partial class Key
{
    public const int MaxLength = 64;

    public const string PatternText = "^[a-z0-9]+(-[a-z0-9]+)*$";

    [GeneratedRegex(PatternText)]
    private static partial Regex Pattern();

    public static bool IsValid(string key) =>
        key is not null && key.Length <= MaxLength && Pattern().IsMatch(key);

    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> does not match <see cref="PatternText"/> or is
    /// longer than <see cref="MaxLength"/>.
    /// </exception>
    public static string Normalize(string key, string parameterName = "key")
    {
        var trimmed = key?.Trim() ?? string.Empty;

        return IsValid(trimmed)
            ? trimmed
            : throw new ArgumentException(
                $"A key is lower case, at most {MaxLength} characters, and hyphens only between them ({PatternText}).",
                parameterName);
    }
}
