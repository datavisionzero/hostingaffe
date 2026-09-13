using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The machine tokens (<c>docs/storage.md</c>, Machine tokens): one live key
/// per machine, and the revoked ones it had before (ADR 0016).
/// </summary>
/// <remarks>
/// The hash and the prefix are what every other token keeps, and the lookup is
/// the same one: the presented secret is hashed and found by the unique index.
/// A revoked row stays, so the partial index is what holds "one per machine" —
/// over the live ones only.
/// </remarks>
public sealed class MachineTokenConfiguration : IEntityTypeConfiguration<MachineToken>
{
    public void Configure(EntityTypeBuilder<MachineToken> builder)
    {
        builder.ToTable("machine_token");

        builder.HasKey(t => t.Id).HasName("pk_machine_token");
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.MachineId).HasColumnName("machine_id").IsRequired();
        builder.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(t => t.MachineId)
            .HasConstraintName("fk_machine_token_machine")
            .OnDelete(DeleteBehavior.NoAction);

        // One live token per machine; the revoked ones stay and are not counted.
        builder.HasIndex(t => t.MachineId)
            .IsUnique()
            .HasFilter("revoked_at is null")
            .HasDatabaseName("machine_token_machine");

        builder.Property(t => t.Prefix).HasColumnName("prefix").IsRequired();

        builder.Property(t => t.SecretHash).HasColumnName("secret_hash").IsRequired();
        builder.HasIndex(t => t.SecretHash).IsUnique().HasDatabaseName("machine_token_secret_hash");

        builder.Property(t => t.IssuedBy).HasColumnName("issued_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(t => t.IssuedBy)
            .HasConstraintName("fk_machine_token_issued_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(t => t.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(t => t.LastUsedAt).HasColumnName("last_used_at");

        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.RevokedBy).HasColumnName("revoked_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(t => t.RevokedBy)
            .HasConstraintName("fk_machine_token_revoked_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(t => t.Revoked);
    }
}
