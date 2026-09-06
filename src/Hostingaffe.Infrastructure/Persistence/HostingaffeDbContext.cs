using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Pages;
using Hostingaffe.Domain.Projects;
using Hostingaffe.Domain.Releases;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The one place that declares schema (<c>docs/codebase.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// EF Core owns every table of <c>docs/storage.md</c> and the migrations that
/// apply themselves on startup. What it does not own is the two things the
/// model cannot say: the <c>issue_read</c> view, through which every read of an
/// issue applies the two derived rules, and the case-insensitive unique index on
/// an identity's name. Both are SQL in the migration that created them, and
/// both are named in the configuration of the table they belong to, so that
/// nobody reading the model believes they are missing.
/// </para>
/// <para>
/// Writes do not go through the view. The conditional updates of the acts —
/// claim, <c>next</c>, close — are written close to the SQL in the stores, in
/// one transaction each, and repeat the two predicates inline because it is the
/// row they lock and change (<c>docs/storage.md</c>, What is derived on read).
/// </para>
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

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectAccess> ProjectAccesses => Set<ProjectAccess>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<Epic> Epics => Set<Epic>();

    public DbSet<EpicLabel> EpicLabels => Set<EpicLabel>();

    public DbSet<Issue> Issues => Set<Issue>();

    /// <summary>
    /// The issues as they are read: through <c>issue_read</c>, with a deleted
    /// issue absent and an expired claim gone. Every read query of an issue
    /// starts here; every write starts at <see cref="Issues"/>.
    /// </summary>
    public DbSet<IssueRead> IssueReads => Set<IssueRead>();

    public DbSet<IssueLabel> IssueLabels => Set<IssueLabel>();

    public DbSet<Blocker> Blockers => Set<Blocker>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Question> Questions => Set<Question>();

    /// <summary>The project's flat wiki (VISION 7, ADR 0021).</summary>
    public DbSet<Page> Pages => Set<Page>();

    public DbSet<PageLabel> PageLabels => Set<PageLabel>();

    public DbSet<Release> Releases => Set<Release>();

    public DbSet<ReleaseIssue> ReleaseIssues => Set<ReleaseIssue>();

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
