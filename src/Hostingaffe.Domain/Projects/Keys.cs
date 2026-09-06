using System.Text.RegularExpressions;

namespace Hostingaffe.Domain.Projects;

/// <summary>
/// The issue key, <c>PLAN-42</c> (<c>CONTEXT.md</c>): the project key and the
/// number, joined at read time and parsed back on the way in. Never stored.
/// </summary>
public static partial class IssueKey
{
    [GeneratedRegex("^([A-Z][A-Z0-9]{1,9})-([1-9][0-9]{0,8})$")]
    private static partial Regex Pattern();

    public static string Of(string projectKey, int number) => $"{projectKey}-{number}";

    public static bool TryParse(string? key, out string projectKey, out int number)
    {
        projectKey = string.Empty;
        number = 0;

        var match = key is null ? null : Pattern().Match(key.Trim().ToUpperInvariant());
        if (match is null || !match.Success)
        {
            return false;
        }

        projectKey = match.Groups[1].Value;
        number = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>The epic key, <c>PLAN-E3</c>, drawn from a sequence of its own.</summary>
public static partial class EpicKey
{
    [GeneratedRegex("^([A-Z][A-Z0-9]{1,9})-E([1-9][0-9]{0,8})$")]
    private static partial Regex Pattern();

    public static string Of(string projectKey, int number) => $"{projectKey}-E{number}";

    public static bool TryParse(string? key, out string projectKey, out int number)
    {
        projectKey = string.Empty;
        number = 0;

        var match = key is null ? null : Pattern().Match(key.Trim().ToUpperInvariant());
        if (match is null || !match.Success)
        {
            return false;
        }

        projectKey = match.Groups[1].Value;
        number = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}
