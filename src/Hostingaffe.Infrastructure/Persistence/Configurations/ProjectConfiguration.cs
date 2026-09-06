using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Pages;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("project");

        builder.HasKey(p => p.Id).HasName("pk_project");
        builder.Property(p => p.Id).HasColumnName("id");

        // Unique across deleted projects as well: while a deleted project waits
        // out its grace period, its key cannot be taken (docs/storage.md).
        builder.Property(p => p.Key).HasColumnName("key").IsRequired();
        builder.HasIndex(p => p.Key).IsUnique().HasDatabaseName("project_key");

        builder.Property(p => p.Name).HasColumnName("name").IsRequired();

        builder.Property(p => p.TriageRequired)
            .HasColumnName("triage_required")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.ReviewRequired)
            .HasColumnName("review_required")
            .HasDefaultValue(false)
            .IsRequired();

        // The page every agent is handed with its ticket (CONTEXT.md,
        // Instructions), by id rather than by slug so that a rename leaves it
        // alone. `set null` on delete is the whole of the cleanup: the purge
        // takes a page whose grace period has passed without asking anybody,
        // and the project simply stops pointing at it.
        builder.Property(p => p.InstructionsPageId).HasColumnName("instructions_page_id");
        builder.HasOne<Page>()
            .WithMany()
            .HasForeignKey(p => p.InstructionsPageId)
            .HasConstraintName("fk_project_instructions_page")
            .OnDelete(DeleteBehavior.SetNull);

        // The two counters every key in the project is drawn from, incremented
        // by one statement under the row's lock in the store — never by the
        // change tracker, which is why nothing in Domain sets them.
        builder.Property(p => p.LastIssueNumber)
            .HasColumnName("last_issue_number")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.LastEpicNumber)
            .HasColumnName("last_epic_number")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(p => p.CreatedBy)
            .HasConstraintName("fk_project_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        builder.Property(p => p.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(p => p.DeletedBy)
            .HasConstraintName("fk_project_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(p => p.Deleted);
    }
}
