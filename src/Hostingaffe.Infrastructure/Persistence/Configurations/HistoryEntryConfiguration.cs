using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The history of everything that has one, in a table whose rows name their
/// subject (<c>docs/storage.md</c>, The history).
/// </summary>
/// <remarks>
/// The subject is a pair — kind and row id — and carries no foreign key,
/// because it points at more than one table. That is also what lets a row
/// outlive what it describes, which is what VISION 7 asks of the history of a
/// deleted machine.
/// </remarks>
public sealed class HistoryEntryConfiguration : IEntityTypeConfiguration<HistoryEntry>
{
    public void Configure(EntityTypeBuilder<HistoryEntry> builder)
    {
        builder.ToTable("history", table =>
            table.HasCheckConstraint("ck_history_subject", "subject in ('page', 'machine', 'software', 'installation')"));

        // Always generated, so that the order of the ids is the order the rows
        // were written and nothing can insert one out of sequence.
        builder.HasKey(h => h.Id).HasName("pk_history");
        builder.Property(h => h.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(h => h.Subject)
            .HasColumnName("subject")
            .HasConversion(new SnakeCaseEnumConverter<HistorySubject>())
            .IsRequired();

        builder.Property(h => h.SubjectId).HasColumnName("subject_id").IsRequired();

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

        // The one way the history is read: everything about one subject, in the
        // order it was written.
        builder.HasIndex(h => new { h.Subject, h.SubjectId, h.Id }).HasDatabaseName("history_subject");
    }
}
