using System.Text.RegularExpressions;

namespace Hostingaffe.Domain.Pages;

/// <summary>
/// The shape of a page's address: lower case letters, digits and single
/// hyphens between them (<c>CONTEXT.md</c>, Slug; ADR 0021).
/// </summary>
/// <remarks>
/// <para>
/// Lower case for the reason a label name is lower case — so that
/// <c>architecture</c> and <c>Architecture</c> cannot both exist in one
/// project — and hyphen-separated because the slug is read aloud in running
/// text, where an underscore or a slash reads as punctuation nobody meant.
/// </para>
/// <para>
/// It is validated, never derived from the title: a title is a sentence and an
/// address is not, and deriving one behind the author's back is what ADR 0021
/// refuses.
/// </para>
/// </remarks>
public static partial class Slug
{
    public const int MaxLength = 100;

    public const string PatternText = "^[a-z0-9]+(-[a-z0-9]+)*$";

    [GeneratedRegex(PatternText)]
    private static partial Regex Pattern();

    public static bool IsValid(string slug) =>
        slug is not null && slug.Length <= MaxLength && Pattern().IsMatch(slug);

    /// <exception cref="ArgumentException">
    /// <paramref name="slug"/> does not match <see cref="PatternText"/> or is
    /// longer than <see cref="MaxLength"/>.
    /// </exception>
    public static string Normalize(string slug, string parameterName = "slug")
    {
        var trimmed = slug?.Trim() ?? string.Empty;

        return IsValid(trimmed)
            ? trimmed
            : throw new ArgumentException(
                $"A slug is lower case, at most {MaxLength} characters, and hyphens only between them ({PatternText}).",
                parameterName);
    }
}
