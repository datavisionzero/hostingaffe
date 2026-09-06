using System.Reflection;

namespace Hostingaffe.Api.Hosting;

/// <summary>
/// What this instance calls itself: the tag it was built from, or
/// <c>0.0.0-dev</c> for a build nobody released (<c>Directory.Build.props</c>).
/// </summary>
/// <remarks>
/// It is the same string in the <c>Hostingaffe-Version</c> header of every
/// response and under <c>GET /api/version</c>, and the CLI compares it with its own
/// to report skew as what it is (ADR 0011). The build metadata after the
/// <c>+</c> — the commit — is not part of the semver the CLI compares, and is
/// left off.
/// </remarks>
public static class InstanceVersion
{
    public const string Header = "Hostingaffe-Version";

    public static readonly string Value = Read();

    private static string Read()
    {
        var informational = typeof(InstanceVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0-dev";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
