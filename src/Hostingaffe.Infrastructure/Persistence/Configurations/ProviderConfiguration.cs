using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>The provider records and their searchable descriptions (ADR 0020).</summary>
public sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    private const string Letters = "key || ' ' || name || ' ' || description";

    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.ToTable("provider");
        builder.HasKey(p => p.Id).HasName("pk_provider");
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.Key).HasColumnName("key").HasMaxLength(Key.MaxLength).IsRequired();
        builder.HasIndex(p => p.Key).IsUnique().HasDatabaseName("provider_key");
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(Provider.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasDefaultValue(string.Empty).IsRequired();
        builder.Property<string>("Letters").HasColumnName("letters").HasComputedColumnSql(Letters, stored: true);
        builder.HasIndex("Letters").HasMethod("GIN").HasOperators("gin_trgm_ops").HasDatabaseName("provider_letters");
        builder.Property<NpgsqlTsVector>("Search").HasColumnName("search")
            .HasComputedColumnSql($"to_tsvector('simple', {Letters})", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("provider_search");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>().WithMany().HasForeignKey(p => p.CreatedBy)
            .HasConstraintName("fk_provider_created_by").OnDelete(DeleteBehavior.NoAction);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.HasOne<Identity>().WithMany().HasForeignKey(p => p.UpdatedBy)
            .HasConstraintName("fk_provider_updated_by").OnDelete(DeleteBehavior.NoAction);
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        builder.Property(p => p.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>().WithMany().HasForeignKey(p => p.DeletedBy)
            .HasConstraintName("fk_provider_deleted_by").OnDelete(DeleteBehavior.NoAction);
        builder.Ignore(p => p.Deleted);
    }
}
