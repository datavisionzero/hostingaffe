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

// The two sinks of ADR 0008, chosen once from three variables (docs/operations.md).
// `writeToProviders` keeps the providers a host adds beside Serilog — a test
// host listening for errors — in the loop.
var logSettings = LogSettings.FromVariables(
    builder.Configuration[LogSettings.EndpointVariable],
    builder.Configuration[LogSettings.TokenVariable],
    builder.Configuration[LogSettings.LevelVariable]);
builder.Host.UseSerilog((_, configuration) => LogSinks.Configure(configuration, logSettings), writeToProviders: true);

builder.Services.AddHostingaffeInfrastructure(builder.Configuration);

var smtpSettings = SmtpSettings.FromVariables(
    builder.Configuration[SmtpSettings.HostVariable],
    builder.Configuration[SmtpSettings.PortVariable],
    builder.Configuration[SmtpSettings.UsernameVariable],
    builder.Configuration[SmtpSettings.PasswordVariable],
    builder.Configuration[SmtpSettings.SecurityVariable],
    builder.Configuration[SmtpSettings.FromAddressVariable],
    builder.Configuration[SmtpSettings.FromNameVariable],
    builder.Configuration[SmtpSettings.PublicUrlVariable],
    builder.Environment.IsDevelopment());
builder.Services.AddSingleton(smtpSettings);

// Who may speak for the caller (docs/operations.md). Unset, nothing may, and
// the instance reads the socket.
var trustedProxies = TrustedProxies.FromVariable(builder.Configuration[TrustedProxies.Variable]);
builder.Services.AddSingleton(trustedProxies);

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

// The two dials of the instance, read once from the environment; a value that
// is not a positive number stops the start here, where the message names it.
builder.Services.AddSingleton(InstanceSettings.FromVariables(
    builder.Configuration[InstanceSettings.ClaimExpiryVariable],
    builder.Configuration[InstanceSettings.DeletionGraceVariable]));

// Projects and their labels: the bracket everything belongs to, and the one
// extensibility the product offers.
builder.Services.AddScoped<CreateProject>();
builder.Services.AddScoped<ListProjects>();
builder.Services.AddScoped<ListAdminProjects>();
builder.Services.AddScoped<ReadProject>();
builder.Services.AddScoped<ChangeProject>();
builder.Services.AddScoped<DeleteProject>();
builder.Services.AddScoped<RestoreProject>();
builder.Services.AddScoped<ProjectScope>();
builder.Services.AddScoped<ListProjectUsers>();
builder.Services.AddScoped<GrantProjectAccess>();
builder.Services.AddScoped<RevokeProjectAccess>();
builder.Services.AddScoped<ListLabels>();
builder.Services.AddScoped<CreateLabel>();
builder.Services.AddScoped<ChangeLabel>();
builder.Services.AddScoped<DeleteLabel>();
builder.Services.AddScoped<RestoreLabel>();

// The issue without its acts: creating several wired-up ones in one transaction
// (VISION 10), the two shapes (ADR 0012), the guarded change, the edges.
builder.Services.AddScoped<IssueAssembler>();
builder.Services.AddScoped<CreateIssues>();
builder.Services.AddScoped<ListIssues>();
builder.Services.AddScoped<ReadIssue>();
builder.Services.AddScoped<ChangeIssue>();
builder.Services.AddScoped<IssueEdges>();

// The claim, which is the whole reason this product exists (VISION 11).
builder.Services.AddScoped<ClaimIssue>();
builder.Services.AddScoped<ReleaseIssue>();
builder.Services.AddScoped<Next>();
builder.Services.AddScoped<NeedsYou>();
builder.Services.AddScoped<MoveIssue>();

// What hangs on an issue beside its fields: comments, questions, the history.
builder.Services.AddScoped<CommentOnIssue>();
builder.Services.AddScoped<EditComment>();
builder.Services.AddScoped<DeleteComment>();
builder.Services.AddScoped<AskQuestion>();
builder.Services.AddScoped<AnswerQuestion>();
builder.Services.AddScoped<ReadQuestion>();
builder.Services.AddScoped<ListQuestions>();
builder.Services.AddScoped<ReadHistory>();

// The bracket: a theme with a living document, whose status gates nothing (VISION 7).
builder.Services.AddScoped<EpicAssembler>();
builder.Services.AddScoped<CreateEpic>();
builder.Services.AddScoped<ListEpics>();
builder.Services.AddScoped<ReadEpic>();
builder.Services.AddScoped<ChangeEpic>();
builder.Services.AddScoped<MoveEpic>();

// The flat wiki (VISION 7, ADR 0021): a project's pages, addressed by slug.
builder.Services.AddScoped<PageAssembler>();
builder.Services.AddScoped<ListPages>();
builder.Services.AddScoped<ReadPage>();
builder.Services.AddScoped<CreatePage>();
builder.Services.AddScoped<ChangePage>();
builder.Services.AddScoped<MovePage>();

builder.Services.AddScoped<ReleaseAssembler>();
builder.Services.AddScoped<ListReleases>();
builder.Services.AddScoped<ReadRelease>();
builder.Services.AddScoped<ChangeRelease>();
builder.Services.AddScoped<PublishRelease>();
builder.Services.AddScoped<RetractRelease>();
builder.Services.AddScoped<ChangeReleaseIssues>();

// Deleting is a soft delete with a floor (ADR 0013); the purge runs at the end of every write transaction.
builder.Services.AddScoped<DeleteIssue>();
builder.Services.AddScoped<RestoreIssue>();

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
    options.SerializerOptions.Converters.Add(new PriorityAsNumber());
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

// Explicit, because two middlewares below read what routing decided rather than
// what the caller typed: the project-scope door reads the endpoint's route
// pattern, and the CSRF guard its `AllowAnonymous` metadata. A host adds this
// by itself, at the front, and both would still work — saying it here is what
// keeps a later reordering from silently moving them in front of it.
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<ProjectScopeMiddleware>();
app.UseMiddleware<BrowserCsrfMiddleware>();
app.UseHostingaffeIdempotency();
app.UseAuthorization();

// Outside the door: the contract is what a client compiles against before it
// has a token, and what CI captures from an instance nobody has bootstrapped.
app.MapOpenApi();

app.MapInstance();
app.MapIdentities();
app.MapBrowserIdentity();
app.MapProjects();
app.MapLabels();
app.MapIssues();
app.MapConversation();
app.MapEpics();
app.MapPages();
app.MapReleases();
app.MapSmtp();

// The web application: built by its own toolchain into wwwroot at image build
// time (deploy/Dockerfile) or by a local `npm run build`; in development the
// Vite dev server serves it and this finds nothing. Every path no endpoint
// took is the SPA's — its router decides what `/PLAN/ready` is.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>
/// Named so that a test can start this instance in its own process. Top level
/// statements produce a class that is otherwise unreachable, and asking a
/// running instance what its endpoints admit is the only way to say it.
/// </summary>
public partial class Program;
