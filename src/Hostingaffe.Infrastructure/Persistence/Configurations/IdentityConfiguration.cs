using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// One table for both kinds (<c>docs/storage.md</c>, Identities and tokens),
/// with the kind as the discriminator column. Names are written out rather than
/// derived from a convention, so that what is in the database reads the same as
/// the DDL in that document.
/// </summary>
/// <remarks>
/// The unique index on <c>lower(name)</c> is not here: EF Core has no model for
/// an expression index, so <c>identity_name</c> is SQL in the migration that
/// created the table. It exists, and this is where a reader learns that.
/// </remarks>
public sealed class IdentityConfiguration : IEntityTypeConfiguration<Identity>
{
    public void Configure(EntityTypeBuilder<Identity> builder)
    {
        builder.ToTable("identity", table =>
        {
            table.HasCheckConstraint("ck_identity_kind", "kind in ('user', 'agent')");

            // An agent has an owner and is never an administrator (ADR 0015); a
            // user has no owner. Held here, not only by the write path.
            table.HasCheckConstraint(
                "ck_identity_owner",
                "kind = 'user' and owner_id is null or kind = 'agent' and owner_id is not null and not administrator");
            table.HasCheckConstraint(
                "ck_identity_user",
                "kind = 'user' and email is not null and normalized_email is not null and user_state in ('invited', 'active', 'deactivated') or kind = 'agent' and email is null and normalized_email is null and user_state is null and password_hash is null and bootstrap_exchanged_at is null");
        });

        builder.HasKey(i => i.Id).HasName("pk_identity");
        builder.Property(i => i.Id).HasColumnName("id");

        builder.HasDiscriminator<string>("kind")
            .HasValue<User>("user")
            .HasValue<Agent>("agent");

        // `text`, as every other string column: the convention would size the
        // column to the longest value it knows, and a third kind would then be
        // a column change as well as a code change.
        builder.Property<string>("kind").HasColumnType("text");

        builder.Property(i => i.Name).HasColumnName("name").IsRequired();

        builder.Property(i => i.Administrator)
            .HasColumnName("administrator")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        // Nullable in the column, because the column is shared with users;
        // required on every agent row, by the check constraint above.
        builder.Property(a => a.OwnerId).HasColumnName("owner_id");

        builder.OwnsOne(a => a.Metadata, metadata =>
        {
            metadata.ToJson("metadata");
            metadata.Property(m => m.Kind).HasJsonPropertyName("kind");
            metadata.Property(m => m.Harness).HasJsonPropertyName("harness");
            metadata.Property(m => m.Environment).HasJsonPropertyName("environment");
            metadata.Property(m => m.Version).HasJsonPropertyName("version");
        });

        builder.Property(a => a.MetadataReportedAt).HasColumnName("metadata_reported_at");

        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(a => a.OwnerId)
            .HasConstraintName("fk_identity_owner")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AgentMetadataReportConfiguration : IEntityTypeConfiguration<AgentMetadataReport>
{
    public void Configure(EntityTypeBuilder<AgentMetadataReport> builder)
    {
        builder.ToTable("identity_metadata");
        builder.HasKey(r => r.Id).HasName("pk_identity_metadata");
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.IdentityId).HasColumnName("identity_id").IsRequired();
        builder.Property(r => r.ReportedAt).HasColumnName("reported_at").IsRequired();
        builder.HasIndex(r => new { r.IdentityId, r.ReportedAt }).HasDatabaseName("identity_metadata_identity");
        builder.HasOne<Agent>().WithMany().HasForeignKey(r => r.IdentityId)
            .HasConstraintName("fk_identity_metadata_identity").OnDelete(DeleteBehavior.NoAction);
        builder.OwnsOne(r => r.Metadata, metadata =>
        {
            metadata.ToJson("metadata");
            metadata.Property(m => m.Kind).HasJsonPropertyName("kind");
            metadata.Property(m => m.Harness).HasJsonPropertyName("harness");
            metadata.Property(m => m.Environment).HasJsonPropertyName("environment");
            metadata.Property(m => m.Version).HasJsonPropertyName("version");
        });
    }
}
