// The composition root. Endpoints, authentication and the log sinks arrive with
// the code they belong to rather than as empty registrations placed here in
// advance.
//
// The OpenAPI document is captured from a running installation and checked in
// (ADR 0005), which is why nothing in this build generates it.

using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Api.Hosting;
using Hostingaffe.Api.Http;
using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;
using Hostingaffe.Infrastructure;
using Serilog;

// Serilog says what is wrong with Serilog here and nowhere else: a sink that
// cannot deliver writes to SelfLog and carries on, so a logaffe that is down or
// a file that cannot be opened costs a line on standard error, never a request.
Serilog.Debugging.SelfLog.Enable(Console.Error);

var builder = WebApplication.CreateBuilder(args);

// Everything read from the environment is read here, in one block, so that a
// value the instance will not accept stops the start with the one line that
// names the variable. Without it the exception escapes unhandled, and what the
// operator finds is a stack trace in a container restarting every few seconds.
// There is no logger yet — this runs before the host is built, which is why the
// message goes to stderr by hand.
TrustedProxies trustedProxies;
try
{
    // The two sinks of ADR 0008, chosen once from three variables
    // (docs/operations.md). `writeToProviders` keeps the providers a host adds
    // beside Serilog — a test host listening for errors — in the loop.
    var logSettings = LogSettings.FromVariables(
        builder.Configuration[LogSettings.EndpointVariable],
        builder.Configuration[LogSettings.TokenVariable],
        builder.Configuration[LogSettings.LevelVariable]);
    builder.Host.UseSerilog((_, configuration) => LogSinks.Configure(configuration, logSettings), writeToProviders: true);

    builder.Services.AddHostingaffeInfrastructure(builder.Configuration);

    builder.Services.AddSingleton(SmtpSettings.FromVariables(
        builder.Configuration[SmtpSettings.HostVariable],
        builder.Configuration[SmtpSettings.PortVariable],
        builder.Configuration[SmtpSettings.UsernameVariable],
        builder.Configuration[SmtpSettings.PasswordVariable],
        builder.Configuration[SmtpSettings.SecurityVariable],
        builder.Configuration[SmtpSettings.FromAddressVariable],
        builder.Configuration[SmtpSettings.FromNameVariable],
        builder.Configuration[SmtpSettings.PublicUrlVariable],
        builder.Environment.IsDevelopment()));

    // Who may speak for the caller (docs/operations.md). Unset, nothing may, and
    // the instance reads the socket.
    trustedProxies = TrustedProxies.FromVariable(builder.Configuration[TrustedProxies.Variable]);
    builder.Services.AddSingleton(trustedProxies);
}
catch (ArgumentException refusal)
{
    Console.Error.WriteLine($"{refusal.Message} The instance will not start.");
    return 1;
}

// The acts are registered here; the layers below know nothing about the
// container they are resolved from. The clock is the base class library's.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton(BrowserCookie.For(builder.Environment.IsDevelopment()));
builder.Services.AddScoped<AuthenticateToken>();
builder.Services.AddScoped<BootstrapTheInstance>();
builder.Services.AddScoped<ReadMe>();
builder.Services.AddScoped<ReportAgentMetadata>();

// The human side of the permission line (docs/api.md, Who may do what): users,
// agents and tokens are a user's acts, users an administrator's.
builder.Services.AddScoped<CreateUser>();
builder.Services.AddScoped<ListUsers>();
builder.Services.AddScoped<RenameUser>();
builder.Services.AddScoped<ResendInvitation>();
builder.Services.AddScoped<ChangeUserLifecycle>();
builder.Services.AddScoped<RequestEmailChange>();
builder.Services.AddScoped<ConfirmEmailChange>();
builder.Services.AddScoped<CreateAgent>();
builder.Services.AddScoped<ListAgents>();
builder.Services.AddScoped<RenameAgent>();
builder.Services.AddScoped<RevokeAgent>();
builder.Services.AddScoped<ListTokens>();
builder.Services.AddScoped<CreateToken>();
builder.Services.AddScoped<RevokeToken>();
builder.Services.AddScoped<ReadSmtpStatus>();
builder.Services.AddScoped<SendTestEmail>();
builder.Services.AddScoped<SignInWithPassword>();
builder.Services.AddScoped<ExchangeBootstrapToken>();
builder.Services.AddScoped<AcceptInvitation>();
builder.Services.AddScoped<RequestPasswordRecovery>();
builder.Services.AddScoped<CompletePasswordRecovery>();
builder.Services.AddScoped<ListBrowserSessions>();
builder.Services.AddScoped<ChangePassword>();

// The dial of the instance, read once from the environment; a value that is
// not a positive number stops the start here, where the message names it.
builder.Services.AddSingleton(InstanceSettings.FromVariables(
    builder.Configuration[InstanceSettings.DeletionGraceVariable]));

// The record itself (VISION 7): the computers the instance knows about.
builder.Services.AddScoped<MachineAssembler>();
builder.Services.AddScoped<ListMachines>();
builder.Services.AddScoped<ReadMachine>();
builder.Services.AddScoped<ReadMachineHistory>();
builder.Services.AddScoped<CreateMachine>();
builder.Services.AddScoped<ChangeMachine>();

// What an installation is an installation of (VISION 7).
builder.Services.AddScoped<SoftwareAssembler>();
builder.Services.AddScoped<ListSoftware>();
builder.Services.AddScoped<ReadSoftware>();
builder.Services.AddScoped<ReadSoftwareHistory>();
builder.Services.AddScoped<CreateSoftware>();
builder.Services.AddScoped<ChangeSoftware>();

// One software installed once on one machine (VISION 7).
builder.Services.AddScoped<InstallationAssembler>();
builder.Services.AddScoped<ListInstallations>();
builder.Services.AddScoped<ReadInstallation>();
builder.Services.AddScoped<ReadInstallationHistory>();
builder.Services.AddScoped<CreateInstallation>();
builder.Services.AddScoped<ChangeInstallation>();

// The text a machine runs with, and every revision it ever had (VISION 7).
builder.Services.AddScoped<FileLookup>();
builder.Services.AddScoped<FileAssembler>();
builder.Services.AddScoped<ListFiles>();
builder.Services.AddScoped<ReadFile>();
builder.Services.AddScoped<ReadFileRevisions>();
builder.Services.AddScoped<ReadFileHistory>();
builder.Services.AddScoped<CreateFile>();
builder.Services.AddScoped<WriteFile>();

// What ran, and when it went live — the source of an installation's version
// (VISION 7).
builder.Services.AddScoped<DeploymentLookup>();
builder.Services.AddScoped<DeploymentAssembler>();
builder.Services.AddScoped<ListDeployments>();
builder.Services.AddScoped<ReadDeployment>();
builder.Services.AddScoped<ReadDeploymentHistory>();
builder.Services.AddScoped<RecordDeployment>();
builder.Services.AddScoped<CorrectDeployment>();

// What a file hangs on and what a page is attached to, resolved in one place.
builder.Services.AddScoped<Anchorage>();

// The flat wiki (VISION 7, ADR 0021): the instance's pages, addressed by slug.
builder.Services.AddScoped<PageAssembler>();
builder.Services.AddScoped<ListPages>();
builder.Services.AddScoped<ReadPage>();
builder.Services.AddScoped<ReadPageHistory>();
builder.Services.AddScoped<CreatePage>();
builder.Services.AddScoped<ChangePage>();
builder.Services.AddScoped<MovePage>();

// Order is start order. The schema first, because the bootstrap reads a table
// the migration may be about to create; both before anything is served, so
// that an installation is `docker compose up` and nothing else (ADR 0011,
// VISION 12).
builder.Services.AddHostedService<SchemaMigrationService>();
builder.Services.AddHostedService<BootstrapService>();

builder.Services.AddHostingaffeTokenAuthentication();
builder.Services.AddHostingaffeOpenApi();

// JSON in, JSON out, snake_case fields (docs/api.md, Conventions). Enums travel
// as the names the contract spells — `in_progress`, `agent` — and integers are
// not accepted alongside, so a value that is not one of the set is refused at
// the door rather than stored as a row nobody can read.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(new Rfc3339());
});

// Every refusal is one document (docs/api.md, Errors), and this is the one
// place that writes it.
builder.Services.AddExceptionHandler<Problems.Handler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

// Before anything reads a scheme or an address: the log line, the CSRF origin
// and the login limit all want the caller's, not the proxy's. Only when an
// operator has named the proxy — an unnamed one is a client with a header.
if (trustedProxies.Configured)
{
    app.UseForwardedHeaders(trustedProxies.Options());
}

// Method, path, status and duration — and nothing an agent wrote (VISION 13).
app.UseSerilogRequestLogging();
app.UseHostingaffeVersion();

// Explicit, because the CSRF guard below reads what routing decided rather than
// what the caller typed: the endpoint's `AllowAnonymous` metadata. A host adds
// this by itself, at the front, and it would still work — saying it here is
// what keeps a later reordering from silently moving it in front.
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<BrowserCsrfMiddleware>();
app.UseHostingaffeIdempotency();
app.UseAuthorization();

// Everything the instance serves as an API is under one prefix, and everything
// else is the web application's (ADR 0002). Both worlds want the word `pages`,
// and `machines`, `installations` and `deployments` are next; the prefix is
// what keeps them from meeting. An endpoint outside this group is a decision,
// not an oversight.
var api = app.MapGroup(Routes.Api);

// Outside the door: the contract is what a client compiles against before it
// has a token, and what CI captures from an instance nobody has bootstrapped.
app.MapOpenApi($"{Routes.Api}/openapi/{{documentName}}.json");

api.MapInstance();
api.MapIdentities();
api.MapBrowserIdentity();
api.MapMachines();
api.MapSoftware();
api.MapInstallations();
api.MapFiles();
api.MapDeployments();
api.MapPages();
api.MapSmtp();

// The web application: built by its own toolchain into wwwroot at image build
// time (deploy/Dockerfile) or by a local `npm run build`; in development the
// Vite dev server serves it and this finds nothing. Every path outside `/api`
// is the SPA's — its router decides what `/pages/architecture` is, and the
// instance never answers that address itself.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

return 0;

/// <summary>
/// Named so that a test can start this instance in its own process. Top level
/// statements produce a class that is otherwise unreachable, and asking a
/// running instance what its endpoints admit is the only way to say it.
/// </summary>
public partial class Program;
