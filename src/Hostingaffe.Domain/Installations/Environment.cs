namespace Hostingaffe.Domain.Installations;

/// <summary>
/// Whom an installation serves (<c>CONTEXT.md</c>, Installation). Closed.
/// </summary>
/// <remarks>
/// <para>
/// It answers a different question from <see cref="Role"/>, which is why both
/// exist: this one is whom, that one is what the installation is for the host.
/// A Caddy in front of production and staging alike is <c>production</c> here
/// and <c>platform</c> there — it carries real traffic, and its ACME data is
/// production data.
/// </para>
/// <para>
/// The name collides with <c>System.Environment</c>, which is implicitly
/// imported everywhere. Every file that needs this one says so with an alias:
/// the model's word is <c>environment</c> (<c>CONTEXT.md</c>), and a type named
/// around the collision would be a word the glossary does not have.
/// </para>
/// </remarks>
public enum Environment
{
    /// <summary>Real traffic, real data.</summary>
    Production,

    /// <summary>A rehearsal of production.</summary>
    Staging,

    /// <summary>Serves nobody but the operator.</summary>
    Development,
}
