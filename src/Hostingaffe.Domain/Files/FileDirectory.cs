namespace Hostingaffe.Domain.Files;

/// <summary>
/// Where a machine's file lies on the machine — <c>/etc/systemd/system</c>,
/// <c>/usr/local/sbin</c>, <c>/etc/docker</c> (VISION 7).
/// </summary>
/// <remarks>
/// <para>
/// An installation has one directory for every file it owns, and it is the
/// installation's own <c>path</c>. A machine has none: its files lie under
/// whatever root the thing that reads them expects, and a record that cannot
/// say which is a record of texts nobody can place. So the directory sits on
/// the file, once per file, and only where the owner is a machine.
/// </para>
/// <para>
/// It is written down, never written to. A file's place on the machine is its
/// owner's directory and its path, and that rule holds for both owners — but
/// <c>files sync</c> writes one directory, and a machine has no single one
/// (ADR 0008).
/// </para>
/// </remarks>
public static class FileDirectory
{
    public const int MaxLength = 500;

    /// <summary>
    /// The directory as it will be stored, or a refusal saying which rule it
    /// broke. One trailing slash is what a person types and means nothing; the
    /// root is the one directory that is a slash.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// It is empty, it is not absolute, or it climbs.
    /// </exception>
    public static string Normalize(string? directory, string parameterName = "directory")
    {
        var trimmed = directory?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "A machine's file lies somewhere on the machine: /etc/systemd/system.", parameterName);
        }

        Fields.InField(parameterName, () => Fields.Line(trimmed, MaxLength, "A directory"));

        if (!trimmed.StartsWith('/'))
        {
            throw new ArgumentException(
                "A directory is where the file lies on the machine, from the root: /etc/systemd/system.", parameterName);
        }

        if (trimmed.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("A directory is a path on the machine; a backslash is not part of one.", parameterName);
        }

        if (trimmed.Split('/').Any(segment => segment is ".." or "."))
        {
            throw new ArgumentException("A directory does not climb; .. and . are not part of one.", parameterName);
        }

        return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }
}
