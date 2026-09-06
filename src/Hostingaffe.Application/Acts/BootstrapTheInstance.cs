using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// What the operator put in the environment for the first start:
/// <c>HOSTINGAFFE_BOOTSTRAP_ADMIN</c>, <c>HOSTINGAFFE_BOOTSTRAP_EMAIL</c> and
/// <c>HOSTINGAFFE_BOOTSTRAP_TOKEN</c>, any of which may be missing.
/// </summary>
public sealed record BootstrapSettings(string? AdministratorName, string? TokenSecret, string? Email = null)
{
    public const string AdministratorVariable = "HOSTINGAFFE_BOOTSTRAP_ADMIN";

    public const string TokenVariable = "HOSTINGAFFE_BOOTSTRAP_TOKEN";
    public const string EmailVariable = "HOSTINGAFFE_BOOTSTRAP_EMAIL";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(AdministratorName) && !string.IsNullOrWhiteSpace(TokenSecret) && !string.IsNullOrWhiteSpace(Email);
}

/// <summary>
/// How the bootstrap ended, for the host to log.
/// </summary>
public enum BootstrapOutcome
{
    /// <summary>The first administrator and their token were created from the environment.</summary>
    Bootstrapped,

    /// <summary>An identity already exists; the environment was ignored, whatever it says.</summary>
    AlreadyBootstrapped,

    /// <summary>
    /// No identity exists and the environment does not name one. The instance
    /// starts anyway, and nothing can authenticate until somebody creates one.
    /// </summary>
    NothingToBootstrapFrom,
}

/// <summary>
/// Thrown when the bootstrap secret is not one the instance will accept: the
/// start is refused, with this message, before anything is written.
/// </summary>
public sealed class BootstrapRefusedException(string message) : Exception(message);

/// <summary>
/// The first administrator and their user token, from the environment, on the
/// first start (VISION 12, ADR 0015, <c>docs/storage.md</c> Bootstrap).
/// </summary>
/// <remarks>
/// <para>
/// There is no setup wizard and no first-run screen — a product whose first
/// principle is that everything works without the UI must not require the UI
/// to become usable. So the operator names the administrator and supplies the
/// secret, and the instance creates both on the one start where the
/// <c>identity</c> table is empty.
/// </para>
/// <para>
/// <strong>On the second start the variables are ignored</strong>, whatever
/// they say. Changing the token in the environment changes nothing; losing it is
/// recovered through the server binary, which has the connection string, and
/// that verb is not in cut one. The check for emptiness comes first for that
/// reason: a short secret on a bootstrapped instance is not a refusal, it is
/// noise.
/// </para>
/// </remarks>
public sealed class BootstrapTheInstance(IIdentities identities, TimeProvider clock)
{
    public async Task<BootstrapOutcome> ExecuteAsync(
        BootstrapSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (await identities.AnyAsync(cancellationToken))
        {
            return BootstrapOutcome.AlreadyBootstrapped;
        }

        if (!settings.IsComplete)
        {
            return BootstrapOutcome.NothingToBootstrapFrom;
        }

        if (!Domain.Identities.TokenSecret.IsAcceptable(settings.TokenSecret))
        {
            throw new BootstrapRefusedException(
                $"{BootstrapSettings.TokenVariable} is {settings.TokenSecret!.Length} characters; "
                + $"it has to be at least {Domain.Identities.TokenSecret.MinimumLength} "
                + $"and at most {Domain.Identities.TokenSecret.MaximumLength}.");
        }

        try { User.NormalizeEmail(settings.Email!); }
        catch (ArgumentException exception) { throw new BootstrapRefusedException($"{BootstrapSettings.EmailVariable} is not a valid email address: {exception.Message}"); }

        var now = clock.GetUtcNow();
        var administrator = User.Create(settings.AdministratorName!, settings.Email!, administrator: true, now);
        var token = Token.Issue(administrator, settings.TokenSecret!, now);

        await identities.AddAsync(administrator, token, cancellationToken);

        return BootstrapOutcome.Bootstrapped;
    }
}
