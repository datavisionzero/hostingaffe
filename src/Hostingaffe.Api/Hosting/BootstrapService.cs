using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Hosting;

/// <summary>
/// Runs the bootstrap after the migrations and before the instance serves:
/// the first administrator and their token from the environment, once.
/// </summary>
/// <remarks>
/// Logged either way (<c>docs/storage.md</c>, Bootstrap): the operator who set
/// the variables on the second start should read that they were ignored, and
/// the one who set neither on the first should read how to proceed. A secret
/// the instance will not accept stops the start, the way a failed migration
/// does — nothing was written, and starting anyway would be an instance nobody
/// can log into with a variable that looks as if they could.
/// </remarks>
public sealed class BootstrapService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = new BootstrapSettings(
            configuration[BootstrapSettings.AdministratorVariable],
            configuration[BootstrapSettings.TokenVariable],
            configuration[BootstrapSettings.EmailVariable]);

        await using var scope = scopeFactory.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<BootstrapTheInstance>();

        BootstrapOutcome outcome;
        try
        {
            outcome = await bootstrap.ExecuteAsync(settings, cancellationToken);
        }
        catch (BootstrapRefusedException refusal)
        {
            logger.LogCritical("{Reason} The instance will not start.", refusal.Message);
            throw;
        }

        switch (outcome)
        {
            case BootstrapOutcome.Bootstrapped:
                logger.LogInformation(
                    "Bootstrapped: the administrator {Name} and their user token were created from "
                    + "{AdministratorVariable} and {TokenVariable}. The token is the one in the "
                    + "environment; the environment is ignored from the next start on.",
                    settings.AdministratorName,
                    BootstrapSettings.AdministratorVariable,
                    BootstrapSettings.TokenVariable);
                break;

            case BootstrapOutcome.AlreadyBootstrapped when settings.AdministratorName is not null || settings.TokenSecret is not null:
                logger.LogInformation(
                    "{AdministratorVariable} and {TokenVariable} are set and ignored: the instance "
                    + "already has identities, and a bootstrap happens once.",
                    BootstrapSettings.AdministratorVariable,
                    BootstrapSettings.TokenVariable);
                break;

            case BootstrapOutcome.AlreadyBootstrapped:
                break;

            case BootstrapOutcome.NothingToBootstrapFrom:
                logger.LogWarning(
                    "No identity exists and nothing can authenticate. Set {AdministratorVariable}, "
                    + "{EmailVariable} and {TokenVariable} (at least {MinimumLength} characters) and start again "
                    + "to create the first administrator and their token.",
                    BootstrapSettings.AdministratorVariable,
                    BootstrapSettings.EmailVariable,
                    BootstrapSettings.TokenVariable,
                    Domain.Identities.TokenSecret.MinimumLength);
                break;

            default:
                throw new InvalidOperationException($"Unknown bootstrap outcome {outcome}.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
