using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Pages;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The history of everything that has one, in a table whose rows name their
/// subject. A page's is the only kind there is yet, and a row dies with the
/// page it belongs to (ADR 0013).
/// </summary>
public sealed class HistoryEntryConfiguration : IEntityTypeConfiguration<HistoryEntry>
{
    public void Configure(EntityTypeBuilder<HistoryEntry> builder)
    {
        builder.ToTable("history");

        // Always generated, so that the order of the ids is the order the rows
        // were written and nothing can insert one out of sequence.
        builder.HasKey(h => h.Id).HasName("pk_history");
        builder.Property(h => h.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(h => h.PageId).HasColumnName("page_id").IsRequired();
        builder.HasOne<Page>()
            .WithMany()
            .HasForeignKey(h => h.PageId)
            .HasConstraintName("fk_history_page")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(h => h.ActorId).HasColumnName("actor_id").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(h => h.ActorId)
            .HasConstraintName("fk_history_actor")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(h => h.At).HasColumnName("at").IsRequired();
        builder.Property(h => h.Field).HasColumnName("field").IsRequired();
        builder.Property(h => h.OldValue).HasColumnName("old_value");
        builder.Property(h => h.NewValue).HasColumnName("new_value");
        builder.Property(h => h.Note).HasColumnName("note");

        builder.HasIndex(h => new { h.PageId, h.Id }).HasDatabaseName("history_page");
    }
}
