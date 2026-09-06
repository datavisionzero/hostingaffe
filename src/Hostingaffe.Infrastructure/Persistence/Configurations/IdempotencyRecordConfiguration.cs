using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency");

        builder.HasKey(r => new { r.IdentityId, r.Key }).HasName("pk_idempotency");
        builder.Property(r => r.IdentityId).HasColumnName("identity_id");
        builder.Property(r => r.Key).HasColumnName("key");

        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(r => r.IdentityId)
            .HasConstraintName("fk_idempotency_identity")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(r => r.RequestHash).HasColumnName("request_hash").IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").IsRequired();
        builder.Property(r => r.Body).HasColumnName("body").HasColumnType("jsonb");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
