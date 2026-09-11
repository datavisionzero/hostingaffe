using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Installations;
using NpgsqlTypes;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The deployments (<c>docs/storage.md</c>, Deployments): what actually ran, and
/// when it went live.
/// </summary>
/// <remarks>
/// A deployment has no key. It is numbered per installation, and
/// <c>deployment_number</c> is what makes that number an address. There is no
/// status column, because a deployment is recorded when it is done.
/// </remarks>
public sealed class DeploymentConfiguration : IEntityTypeConfiguration<Deployment>
{
    /// <inheritdoc cref="MachineConfiguration.Letters"/>
    private const string Letters =
        "version || ' ' || coalesce(\"ref\", '') || ' ' || coalesce(ticket, '') || ' ' || note";

    public void Configure(EntityTypeBuilder<Deployment> builder)
    {
        builder.ToTable("deployment", table =>
            table.HasCheckConstraint("ck_deployment_number", "number >= 1"));

        builder.HasKey(d => d.Id).HasName("pk_deployment");
        builder.Property(d => d.Id).HasColumnName("id");

        builder.Property(d => d.InstallationId).HasColumnName("installation_id").IsRequired();
        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(d => d.InstallationId)
            .HasConstraintName("fk_deployment_installation")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(d => d.Number).HasColumnName("number").IsRequired();

        // The number is the address, and it is one per installation.
        builder.HasIndex(d => new { d.InstallationId, d.Number })
            .IsUnique()
            .HasDatabaseName("deployment_number");

        // The order everything derived is computed in: by `at`, and the number
        // breaks a tie (VISION 7).
        builder.HasIndex(d => new { d.InstallationId, d.At, d.Number })
            .IsDescending(false, true, true)
            .HasDatabaseName("deployment_when");

        builder.Property(d => d.Version).HasColumnName("version").HasMaxLength(Deployment.VersionMaxLength).IsRequired();
        builder.Property(d => d.Ref).HasColumnName("ref").HasMaxLength(Deployment.RefMaxLength);
        builder.Property(d => d.At).HasColumnName("at").IsRequired();
        builder.Property(d => d.Ticket).HasColumnName("ticket").HasMaxLength(Deployment.TicketMaxLength);

        builder.Property(d => d.Note)
            .HasColumnName("note")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        // `by` on the wire: who recorded it, which for a backfilled deployment
        // is not necessarily who deployed.
        // A deployment is found by what it says it was: the version, what
        // was actually deployed, the ticket, and the note beside it.
        builder.Property<string>("Letters")
            .HasColumnName("letters")
            .HasComputedColumnSql(Letters, stored: true);
        builder.HasIndex("Letters").HasMethod("GIN").HasOperators("gin_trgm_ops").HasDatabaseName("deployment_letters");

        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql($"to_tsvector('simple', {Letters})", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("deployment_search");

        builder.Property(d => d.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(d => d.CreatedBy)
            .HasConstraintName("fk_deployment_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(d => d.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(d => d.UpdatedBy)
            .HasConstraintName("fk_deployment_updated_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(d => d.DeletedAt).HasColumnName("deleted_at");

        builder.Property(d => d.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(d => d.DeletedBy)
            .HasConstraintName("fk_deployment_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(d => d.Deleted);
    }
}
