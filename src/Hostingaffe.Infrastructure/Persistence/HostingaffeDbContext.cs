using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Pages;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The one place that declares schema (<c>docs/codebase.md</c>).
/// </summary>
/// <remarks>
/// EF Core owns every table of <c>docs/storage.md</c> and the migrations that
/// apply themselves on startup. What it does not own is the one thing the model
/// cannot say: the case-insensitive unique index on an identity's name. That is
/// SQL in the migration that created it, and it is named in the configuration
/// of the table it belongs to, so that nobody reading the model believes it is
/// missing.
/// </remarks>
public sealed class HostingaffeDbContext(DbContextOptions<HostingaffeDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Users and agents in one table, one hierarchy, so that every author,
    /// holder and history row points at one place.
    /// </summary>
    public DbSet<Identity> Identities => Set<Identity>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<AgentMetadataReport> AgentMetadataReports => Set<AgentMetadataReport>();

    public DbSet<Token> Tokens => Set<Token>();
    public DbSet<OneTimeSecret> OneTimeSecrets => Set<OneTimeSecret>();
    public DbSet<BrowserSession> BrowserSessions => Set<BrowserSession>();

    /// <summary>The computers the instance is a record of (VISION 7).</summary>
    public DbSet<Machine> Machines => Set<Machine>();

    /// <summary>
    /// What an installation is an installation of (VISION 7). Singular, because
    /// the word is: <c>CONTEXT.md</c> circumscribes the plural rather than
    /// inventing one.
    /// </summary>
    public DbSet<Software> Software => Set<Software>();

    /// <summary>One software installed once on one machine (VISION 7).</summary>
    public DbSet<Installation> Installations => Set<Installation>();

    /// <summary>What actually ran on an installation, and when it went live (VISION 7).</summary>
    public DbSet<Deployment> Deployments => Set<Deployment>();

    /// <summary>The text files a machine runs with, each with every revision it ever had (VISION 7).</summary>
    public DbSet<Domain.Files.File> Files => Set<Domain.Files.File>();

    /// <summary>The instance's flat wiki (VISION 7, ADR 0021).</summary>
    public DbSet<Page> Pages => Set<Page>();

    public DbSet<HistoryEntry> History => Set<HistoryEntry>();

    /// <summary>
    /// What a replayed write is answered from for 24 hours (<c>docs/api.md</c>,
    /// Idempotency). Not a Domain type: nothing the vision states is a rule
    /// about it, and it exists only for the HTTP adapter.
    /// </summary>
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every index is declared, none is inferred. The convention would put
        // one on every foreign key — `created_by`, `deleted_by`, `author_id` —
        // and nothing reads by those; what is read by is in docs/storage.md,
        // and that list is the schema.
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HostingaffeDbContext).Assembly);
}
