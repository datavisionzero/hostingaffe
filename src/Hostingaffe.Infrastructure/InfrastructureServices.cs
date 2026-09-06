using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Hostingaffe.Application.Ports;
using Hostingaffe.Infrastructure.Email;
using Hostingaffe.Infrastructure.Security;
using Hostingaffe.Infrastructure.Persistence;

namespace Hostingaffe.Infrastructure;

/// <summary>
/// What this layer offers the composition root.
/// </summary>
public static class InfrastructureServices
{
    /// <summary>
    /// The connection string's name in configuration — as an environment
    /// variable, <c>ConnectionStrings__Postgres</c>.
    /// </summary>
    public const string ConnectionStringName = "Postgres";

    public static IServiceCollection AddHostingaffeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Read when the context is first built rather than when it is
        // registered: registering must stay free of side effects, because the
        // OpenAPI tooling builds the host at compile time and has no database.
        services.AddDbContext<HostingaffeDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not configured.")));

        services.AddScoped<SchemaMigrator>();

        services.AddScoped<IIdentities, Identities>();
        services.AddScoped<ITokens, Tokens>();
        services.AddScoped<IMachines, Machines>();
        services.AddScoped<ISoftware, SoftwareRows>();
        services.AddScoped<IPages, Pages>();
        services.AddScoped<IHistory, History>();
        services.AddScoped<ITransactions, Transactions>();
        services.AddScoped<IIdempotency, Idempotency>();
        services.AddScoped<IOneTimeSecrets, OneTimeSecrets>();
        services.AddScoped<IBrowserSessions, BrowserSessions>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddTransient<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
