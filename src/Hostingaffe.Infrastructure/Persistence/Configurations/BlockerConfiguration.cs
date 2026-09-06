using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// One directed edge, read from both ends; it may cross projects, so nothing
/// here mentions one. A cycle is refused by the store on write, with a bounded
/// recursive walk; the one-edge cycle is refused here as well.
/// </summary>
public sealed class BlockerConfiguration : IEntityTypeConfiguration<Blocker>
{
    public void Configure(EntityTypeBuilder<Blocker> builder)
    {
        builder.ToTable("blocker", table =>
            table.HasCheckConstraint("ck_blocker_not_self", "blocker_id <> blocked_id"));

        builder.HasKey(b => new { b.BlockerId, b.BlockedId }).HasName("pk_blocker");
        builder.Property(b => b.BlockerId).HasColumnName("blocker_id");
        builder.Property(b => b.BlockedId).HasColumnName("blocked_id");

        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(b => b.BlockerId)
            .HasConstraintName("fk_blocker_blocker")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(b => b.BlockedId)
            .HasConstraintName("fk_blocker_blocked")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(b => b.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(b => b.CreatedBy)
            .HasConstraintName("fk_blocker_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();

        // The primary key reads from the blocker's end; this reads from the
        // blocked one, which is the end `blocked_by` and the `next` predicate ask.
        builder.HasIndex(b => b.BlockedId).HasDatabaseName("blocker_blocked");
    }
}
