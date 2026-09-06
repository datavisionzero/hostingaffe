using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The whole instance in the test process, on a database of its own, with the
/// environment an operator would have set for the first start.
/// </summary>
/// <remarks>
/// The bootstrap variables are always set — to nothing when the test wants none
/// — so that a variable in the developer's real environment cannot reach the
/// test. Starting the host runs the migrations and the bootstrap, which is why
/// a test about a refused start asks for a client and expects the throw.
/// </remarks>
internal sealed class AnInstance(
    string connectionString,
    string? administrator,
    string? token,
    IReadOnlyDictionary<string, string?>? instanceConfiguration = null) : WebApplicationFactory<Program>
{
    public const string Administrator = "maintainer";

    public const string BootstrapToken = "a-bootstrap-secret-of-thirty-two-characters-or-more";

    public static Task<AnInstance> BootstrappedAsync(PostgresFixture postgres) =>
        StartedAsync(postgres, Administrator, BootstrapToken);

    public static async Task<AnInstance> StartedAsync(
        PostgresFixture postgres, string? administrator, string? token) =>
        new(await postgres.CreateDatabaseAsync(), administrator, token);

    public static async Task<AnInstance> ConfiguredAsync(
        PostgresFixture postgres, IReadOnlyDictionary<string, string?> configuration) =>
        new(await postgres.CreateDatabaseAsync(), Administrator, BootstrapToken, configuration);

    /// <summary>The same database, started again with what the environment says now.</summary>
    public AnInstance StartedAgain(string? newAdministrator, string? newToken) =>
        new(connectionString, newAdministrator, newToken, instanceConfiguration);

    public string ConnectionString => connectionString;

    public async Task<string> AddActiveUserAsync(string name, bool administrator = false)
    {
        await using var context = Migrated.ContextFor(connectionString);
        var now = DateTimeOffset.UtcNow;
        var user = User.Create(name, $"{name.ToLowerInvariant().Replace(' ', '.')}@example.test", administrator, now);
        var secret = TokenSecret.Generate();
        context.Users.Add(user);
        context.Tokens.Add(Token.Issue(user, secret, now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return secret;
    }

    /// <summary>
    /// What the instance logged at error or above — the exception behind a 500,
    /// which the problem document deliberately does not carry.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    private readonly List<string> _errors = [];

    public HttpClient ClientWith(string? bearer)
    {
        var client = CreateClient();
        if (bearer is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return client;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
                ["HOSTINGAFFE_BOOTSTRAP_ADMIN"] = administrator ?? string.Empty,
                ["HOSTINGAFFE_BOOTSTRAP_TOKEN"] = token ?? string.Empty,
                ["HOSTINGAFFE_BOOTSTRAP_EMAIL"] = "maintainer@example.test",
            };
            if (instanceConfiguration is not null)
            {
                foreach (var pair in instanceConfiguration)
                {
                    values[pair.Key] = pair.Value;
                }
            }
            configuration.AddInMemoryCollection(values);
        });

        builder.ConfigureLogging(logging => logging.AddProvider(new Capture(_errors)));

        return base.CreateHost(builder);
    }

    private sealed class Capture(List<string> errors) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                lock (errors)
                {
                    errors.Add($"{formatter(state, exception)}\n{exception}");
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
