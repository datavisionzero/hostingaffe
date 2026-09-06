using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The register of keys that have been given out
/// (<c>docs/storage.md</c>, Assigned keys).
/// </summary>
/// <remarks>
/// Two columns and a primary key over both, which is what enforces the rule
/// rather than leaving it to a query somebody can forget. Nothing points at
/// these rows and they point at nothing: the purge does not touch them, and
/// that is the whole of their job.
/// </remarks>
public sealed class AssignedKeyConfiguration : IEntityTypeConfiguration<AssignedKey>
{
    public void Configure(EntityTypeBuilder<AssignedKey> builder)
    {
        builder.ToTable("assigned_key", table =>
            table.HasCheckConstraint("ck_assigned_key_kind", "kind in ('machine', 'software', 'installation')"));

        builder.HasKey(a => new { a.Kind, a.Key }).HasName("pk_assigned_key");

        builder.Property(a => a.Kind)
            .HasColumnName("kind")
            .HasConversion(new SnakeCaseEnumConverter<Keyed>())
            .IsRequired();

        builder.Property(a => a.Key).HasColumnName("key").HasMaxLength(Key.MaxLength).IsRequired();
    }
}
