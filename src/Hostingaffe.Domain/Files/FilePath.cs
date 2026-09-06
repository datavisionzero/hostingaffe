using System.Text.RegularExpressions;

namespace Hostingaffe.Domain.Files;

/// <summary>
/// Where a file sits under its owner, and the one list of paths this product
/// refuses (VISION 7, 10).
/// </summary>
/// <remarks>
/// <para>
/// The list is here, in the Domain, and not in the CLI: the API is open to
/// whoever holds a token, and a rule kept by one client is kept by nobody. The
/// warning about content that <em>looks</em> like a private key is the CLI's,
/// and VISION 7 says itself that it is a guard against accidents rather than a
/// boundary. This is the boundary.
/// </para>
/// <para>
/// <c>.envrc</c> is welcome on purpose: in the template it is one line and
/// carries no value.
/// </para>
/// </remarks>
public static partial class FilePath
{
    public const int MaxLength = 500;

    /// <summary>The directory whose contents are refused whole.</summary>
    public const string SecretsDirectory = "secrets";

    /// <summary>The one <c>.env</c> file that carries no secret.</summary>
    public const string EnvExample = ".env.example";

    private const string SegmentPattern = @"^[A-Za-z0-9._][A-Za-z0-9._~@+-]*$";

    [GeneratedRegex(SegmentPattern)]
    private static partial Regex Segment();

    /// <summary>
    /// The path as it will be stored, or a refusal saying which rule it broke.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// It is absolute, it climbs, it names a secret-bearing path, or a segment
    /// is not the shape of one.
    /// </exception>
    public static string Normalize(string path, string parameterName = "path")
    {
        var trimmed = path?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A file has a path under its owner.", parameterName);
        }

        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentException($"A path is at most {MaxLength} characters.", parameterName);
        }

        if (trimmed.StartsWith('/') || trimmed.Contains('\\', StringComparison.Ordinal) || trimmed.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A path is relative to the owner's directory: no leading slash, no drive, no backslash.", parameterName);
        }

        var segments = trimmed.Split('/');

        foreach (var segment in segments)
        {
            if (segment is ".." or ".")
            {
                throw new ArgumentException(
                    "A path stays inside the owner's directory; .. and . are not part of one.", parameterName);
            }

            if (!Segment().IsMatch(segment))
            {
                throw new ArgumentException(
                    "A path is segments of letters, digits and . _ - ~ @ +, separated by single slashes.", parameterName);
            }
        }

        // Anything *under* secrets/, at any depth. The directory is refused, not
        // a file that happens to be called `secrets`.
        if (segments[..^1].Any(segment => string.Equals(segment, SecretsDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"Nothing under {SecretsDirectory}/ is kept here; the secrets themselves live in vaultaffe or on the host.",
                parameterName);
        }

        var name = segments[^1];

        if (IsEnvironmentFile(name))
        {
            throw new ArgumentException(
                $"{name} carries values, not configuration; only {EnvExample} is kept here (VISION 7). .envrc is welcome.",
                parameterName);
        }

        return trimmed;
    }

    public static bool IsValid(string path)
    {
        try
        {
            Normalize(path);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// <c>.env</c> and every <c>.env.*</c> but <c>.env.example</c>. Not
    /// <c>.envrc</c>, which is a shell file and not a bag of values.
    /// </summary>
    private static bool IsEnvironmentFile(string name) =>
        string.Equals(name, ".env", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, EnvExample, StringComparison.OrdinalIgnoreCase));
}
