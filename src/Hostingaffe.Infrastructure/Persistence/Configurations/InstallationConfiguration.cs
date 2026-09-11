using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;
using NpgsqlTypes;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The installations (<c>docs/storage.md</c>, Installations). The unique index
/// on the key covers deleted rows on purpose: a key stays spent until the purge,
/// so that a restore never lands on a name somebody else has taken (ADR 0013).
/// </summary>
/// <remarks>
/// The ports are a table of their own rather than a column of text or of JSON,
/// because <c>protocol</c> and <c>scope</c> are closed sets like every other one
/// in the model, and a closed set is a column with a check constraint that lists
/// the words. The secrets are a table for the neighbouring reason: a secret is
/// two facts now — the name and the file it lies in — and two facts do not fit
/// in an entry of a <c>text[]</c> (ADR 0011).
/// </remarks>
public sealed class InstallationConfiguration : IEntityTypeConfiguration<Installation>
{
    /// <inheritdoc cref="MachineConfiguration.Letters"/>
    private const string Letters =
        "key || ' ' || name || ' ' || coalesce(path, '') || ' ' "
        + "|| coalesce(data, '') || ' ' || words(urls) || ' ' || description";

    /// <inheritdoc cref="MachineConfiguration.Letters"/>
    private const string SecretLetters = "name || ' ' || coalesce(path, '')";

    public void Configure(EntityTypeBuilder<Installation> builder)
    {
        builder.ToTable("installation", table =>
        {
            table.HasCheckConstraint("ck_installation_environment", "environment in ('production', 'staging', 'development')");
            table.HasCheckConstraint("ck_installation_role", "role in ('application', 'platform')");
            table.HasCheckConstraint("ck_installation_status", "status in ('planned', 'active', 'retired')");
            table.HasCheckConstraint("ck_installation_backup", "backup in ('none', 'planned', 'active')");
            table.HasCheckConstraint("ck_installation_monitoring", "monitoring in ('none', 'planned', 'external')");
            table.HasCheckConstraint("ck_installation_logging", "logging in ('local', 'central')");
        });

        builder.HasKey(i => i.Id).HasName("pk_installation");
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.Key).HasColumnName("key").HasMaxLength(Key.MaxLength).IsRequired();

        // One index for both jobs: it holds the key unique and it is the order
        // the list is read in.
        builder.HasIndex(i => i.Key).IsUnique().HasDatabaseName("installation_key");

        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(Installation.NameMaxLength).IsRequired();

        // Two of the three relationships the model builds; the third is
        // `installation_depends_on` below. No cascade: what a deleted machine
        // does to the installations on it is the soft delete's business, not the
        // database's.
        builder.Property(i => i.MachineId).HasColumnName("machine_id").IsRequired();
        builder.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(i => i.MachineId)
            .HasConstraintName("fk_installation_machine")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(i => i.MachineId).HasDatabaseName("installation_machine");

        builder.Property(i => i.SoftwareId).HasColumnName("software_id").IsRequired();
        builder.HasOne<Software>()
            .WithMany()
            .HasForeignKey(i => i.SoftwareId)
            .HasConstraintName("fk_installation_software")
            .OnDelete(DeleteBehavior.NoAction);

        // Read by whenever a software is asked what hangs on it — which is what
        // refuses its deletion.
        builder.HasIndex(i => i.SoftwareId).HasDatabaseName("installation_software");

        builder.Property(i => i.Environment)
            .HasColumnName("environment")
            .HasConversion(new SnakeCaseEnumConverter<Domain.Installations.Environment>())
            .IsRequired();

        builder.Property(i => i.Role)
            .HasColumnName("role")
            .HasConversion(new SnakeCaseEnumConverter<Role>())
            .IsRequired();

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion(new SnakeCaseEnumConverter<Status>())
            .IsRequired();

        builder.Property(i => i.Backup)
            .HasColumnName("backup")
            .HasConversion(new SnakeCaseEnumConverter<Backup>())
            .IsRequired();

        builder.Property(i => i.Monitoring)
            .HasColumnName("monitoring")
            .HasConversion(new SnakeCaseEnumConverter<Monitoring>())
            .IsRequired();

        builder.Property(i => i.Logging)
            .HasColumnName("logging")
            .HasConversion(new SnakeCaseEnumConverter<Logging>())
            .IsRequired();

        // One list of plain text, held as an array: nothing is read by it, and a
        // join per URL would buy nothing the row does not already say.
        builder.Property(i => i.Urls).HasColumnName("urls").HasDefaultValue(Array.Empty<string>()).IsRequired();

        // Two directories, because an installation has two: the one it is
        // deployed from and the one its state lies in (ADR 0009). Nothing in the
        // column holds them apart — on a host that keeps both together they are
        // the same string.
        builder.Property(i => i.Path).HasColumnName("path").HasMaxLength(Fields.PathMaxLength);
        builder.Property(i => i.Data).HasColumnName("data").HasMaxLength(Fields.PathMaxLength);

        builder.Property(i => i.Description)
            .HasColumnName("description")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.OwnsMany(i => i.Ports, port =>
        {
            port.ToTable("installation_port", table =>
            {
                table.HasCheckConstraint("ck_installation_port_protocol", "protocol in ('tcp', 'udp')");
                table.HasCheckConstraint("ck_installation_port_scope", "scope in ('public', 'private', 'internal')");
                table.HasCheckConstraint("ck_installation_port_number", "port between 1 and 65535");
            });

            port.WithOwner().HasForeignKey("installation_id").HasConstraintName("fk_installation_port_installation");
            port.Property(p => p.Number).HasColumnName("port");
            port.Property(p => p.Protocol).HasColumnName("protocol").HasConversion(new SnakeCaseEnumConverter<Protocol>());
            port.Property(p => p.Scope).HasColumnName("scope").HasConversion(new SnakeCaseEnumConverter<Scope>());

            // The key is what makes a port the same port: the number and the
            // transport. A second row for 443/tcp with another scope would be a
            // contradiction, and the database refuses to hold one.
            port.HasKey("installation_id", nameof(Port.Number), nameof(Port.Protocol)).HasName("pk_installation_port");
        });

        builder.Navigation(i => i.Ports).AutoInclude();

        // A secret is a row for the reason a port is: it stopped being a word
        // the day it got a second half to say (ADR 0011). The name is what makes
        // two of them the same secret, so it is the key and the file is not —
        // one secret lies in one place.
        builder.OwnsMany(i => i.Secrets, secret =>
        {
            secret.ToTable("installation_secret");

            secret.WithOwner().HasForeignKey("installation_id").HasConstraintName("fk_installation_secret_installation");
            secret.Property(s => s.Name).HasColumnName("name").HasMaxLength(Secret.NameMaxLength);
            secret.Property(s => s.Path).HasColumnName("path").HasMaxLength(Fields.PathMaxLength);
            secret.HasKey("installation_id", nameof(Secret.Name)).HasName("pk_installation_secret");

            // Searched on its own row, because a generated column reads its own
            // row and nothing else. That is what keeps `ha search
            // POSTGRES_PASSWORD` — and the file it lies in — answering after the
            // names left the installation's own column.
            secret.Property<string>("Letters")
                .HasColumnName("letters")
                .HasComputedColumnSql(SecretLetters, stored: true);
            secret.HasIndex("Letters").HasMethod("GIN").HasOperators("gin_trgm_ops").HasDatabaseName("installation_secret_letters");

            secret.Property<NpgsqlTsVector>("Search")
                .HasColumnName("search")
                .HasComputedColumnSql($"to_tsvector('simple', {SecretLetters})", stored: true);
            secret.HasIndex("Search").HasMethod("GIN").HasDatabaseName("installation_secret_search");
        });

        builder.Navigation(i => i.Secrets).AutoInclude();

        // The third relationship, and the only one that points at this same
        // table (ADR 0014). It is a row and not a `text[]` of keys for the
        // reason every other relationship is a foreign key: a key in an array
        // names something the database cannot be asked to hold on to, and
        // "which installations depend on this one" — the question the whole
        // edge exists for — would be a scan rather than an index read.
        builder.OwnsMany(i => i.DependsOn, dependency =>
        {
            dependency.ToTable("installation_depends_on");

            dependency.WithOwner().HasForeignKey("installation_id")
                .HasConstraintName("fk_installation_depends_on_installation");
            dependency.Property(d => d.DependsOnId).HasColumnName("depends_on_id");

            // The pair is the row: an installation depends on another one once,
            // and a second row saying it again would be nothing new.
            dependency.HasKey("installation_id", nameof(Dependency.DependsOnId))
                .HasName("pk_installation_depends_on");

            // No cascade here either, and for a sharper reason than above: an
            // installation others depend on is refused deletion outright
            // (DeletionActs), so nothing is ever swept out from under a
            // dependent — and the soft delete leaves the row standing, which is
            // what a restore needs.
            dependency.HasOne<Installation>()
                .WithMany()
                .HasForeignKey(d => d.DependsOnId)
                .HasConstraintName("fk_installation_depends_on_target")
                .OnDelete(DeleteBehavior.NoAction);

            // Read from the other side on every `needed_by`, which is the
            // direction a person actually asks in.
            dependency.HasIndex(d => d.DependsOnId).HasDatabaseName("installation_depends_on_target");
        });

        builder.Navigation(i => i.DependsOn).AutoInclude();

        // The ports and the secrets are tables of their own: a port is searched
        // as a number, looked up in its column — which is what makes VISION 5's
        // example answer at all — and a secret carries a vector of its own row.
        //
        // `words` is the one function the schema owns, and it exists because
        // Postgres marks `array_to_string` stable rather than immutable, which
        // a generated column will not take. For a `text[]` and a constant
        // separator the result depends on nothing, and the migration that
        // creates it says so.
        builder.Property<string>("Letters")
            .HasColumnName("letters")
            .HasComputedColumnSql(Letters, stored: true);
        builder.HasIndex("Letters").HasMethod("GIN").HasOperators("gin_trgm_ops").HasDatabaseName("installation_letters");

        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql($"to_tsvector('simple', {Letters})", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("installation_search");

        builder.Property(i => i.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(i => i.CreatedBy)
            .HasConstraintName("fk_installation_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(i => i.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(i => i.UpdatedBy)
            .HasConstraintName("fk_installation_updated_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(i => i.DeletedAt).HasColumnName("deleted_at");

        builder.Property(i => i.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(i => i.DeletedBy)
            .HasConstraintName("fk_installation_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(i => i.Deleted);
    }
}
