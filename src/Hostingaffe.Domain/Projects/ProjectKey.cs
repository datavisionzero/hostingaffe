using System.Text.RegularExpressions;

namespace Hostingaffe.Domain.Projects;

/// <summary>
/// The short prefix identifying a project and beginning every key inside it,
/// e.g. <c>PLAN</c> (<c>CONTEXT.md</c>, Project key).
/// </summary>
/// <remarks>
/// Upper case, no hyphen, because the hyphen is what separates it from the
/// number in <c>PLAN-42</c> and from the <c>E</c> in <c>PLAN-E3</c>. It is the
/// one thing never changed after creation (ADR 0015), which is why there is no
/// act that renames it.
/// </remarks>
public static partial class ProjectKey
{
    public const string PatternText = "^[A-Z][A-Z0-9]{1,9}$";

    [GeneratedRegex(PatternText)]
    private static partial Regex Pattern();

    public static bool IsValid(string key) => key is not null && Pattern().IsMatch(key);

    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> does not match <see cref="PatternText"/>.
    /// </exception>
    public static string Normalize(string key)
    {
        var trimmed = key?.Trim() ?? string.Empty;

        return IsValid(trimmed)
            ? trimmed
            : throw new ArgumentException(
                $"A project key is upper case, starts with a letter and is two to ten characters ({PatternText}).",
                nameof(key));
    }
}
