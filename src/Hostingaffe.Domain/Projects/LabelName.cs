using System.Text.RegularExpressions;

namespace Hostingaffe.Domain.Projects;

/// <summary>
/// The shape of a label's name and of a label group's: lower case, so that
/// <c>area:infra</c> and <c>Area:Infra</c> cannot both exist
/// (<c>docs/storage.md</c>, Labels).
/// </summary>
public static partial class LabelName
{
    public const string PatternText = "^[a-z0-9][a-z0-9:._/-]{0,49}$";

    [GeneratedRegex(PatternText)]
    private static partial Regex Pattern();

    public static bool IsValid(string name) => name is not null && Pattern().IsMatch(name);

    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not match <see cref="PatternText"/>.
    /// </exception>
    public static string Normalize(string name, string parameterName = "name")
    {
        var trimmed = name?.Trim() ?? string.Empty;

        return IsValid(trimmed)
            ? trimmed
            : throw new ArgumentException(
                $"A label name is lower case, one to fifty characters ({PatternText}).", parameterName);
    }
}
