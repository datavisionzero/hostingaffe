using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;
using NpgsqlTypes;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The software (<c>docs/storage.md</c>, Software). The unique index on the key
/// covers deleted rows on purpose: a key stays spent until the purge, so that a
/// restore never lands on a name somebody else has taken (ADR 0013).
/// </summary>
/// <remarks>
/// There is no version column here and there will not be one. A software
/// carries no version — versions belong to deployments (VISION 7) — and a
/// column that repeated one would be the second truth nobody keeps.
/// </remarks>
public sealed class SoftwareConfiguration : IEntityTypeConfiguration<Software>
{
    /// <inheritdoc cref="MachineConfiguration.Letters"/>
    private const string Letters =
        "key || ' ' || name || ' ' || coalesce(image, '') || ' ' "
        + "|| coalesce(homepage, '') || ' ' || coalesce(repository, '') || ' ' || description";

    public void Configure(EntityTypeBuilder<Software> builder)
    {
        builder.ToTable("software");

        builder.HasKey(s => s.Id).HasName("pk_software");
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.Key).HasColumnName("key").HasMaxLength(Key.MaxLength).IsRequired();

        // One index for both jobs: it holds the key unique and it is the order
        // the list is read in.
        builder.HasIndex(s => s.Key).IsUnique().HasDatabaseName("software_key");

        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(Software.NameMaxLength).IsRequired();

        builder.Property(s => s.Homepage).HasColumnName("homepage").HasMaxLength(Software.ReferenceMaxLength);
        builder.Property(s => s.Repository).HasColumnName("repository").HasMaxLength(Software.ReferenceMaxLength);
        builder.Property(s => s.Image).HasColumnName("image").HasMaxLength(Software.ReferenceMaxLength);

        builder.Property(s => s.Description)
            .HasColumnName("description")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        /// <inheritdoc cref="MachineConfiguration"/>
        builder.Property<string>("Letters")
            .HasColumnName("letters")
            .HasComputedColumnSql(Letters, stored: true);
        builder.HasIndex("Letters").HasMethod("GIN").HasOperators("gin_trgm_ops").HasDatabaseName("software_letters");

        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql($"to_tsvector('simple', {Letters})", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("software_search");

        builder.Property(s => s.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(s => s.CreatedBy)
            .HasConstraintName("fk_software_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(s => s.UpdatedBy)
            .HasConstraintName("fk_software_updated_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(s => s.DeletedAt).HasColumnName("deleted_at");

        builder.Property(s => s.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(s => s.DeletedBy)
            .HasConstraintName("fk_software_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(s => s.Deleted);
    }
}
