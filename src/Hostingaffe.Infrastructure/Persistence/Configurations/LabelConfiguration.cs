using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("label");

        builder.HasKey(l => l.Id).HasName("pk_label");
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.ProjectId).HasColumnName("project_id").IsRequired();
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(l => l.ProjectId)
            .HasConstraintName("fk_label_project")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(l => l.Name).HasColumnName("name").IsRequired();
        builder.HasIndex(l => new { l.ProjectId, l.Name }).IsUnique().HasDatabaseName("label_name");

        // `group` is a reserved word; the column says what kind of group.
        builder.Property(l => l.Group).HasColumnName("label_group");

        builder.Property(l => l.Description).HasColumnName("description");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.DeletedAt).HasColumnName("deleted_at");

        builder.Ignore(l => l.Deleted);
    }
}

/// <summary>
/// The attachments stay when a label is soft-deleted, and go with the label or
/// the issue when either is purged — the cascades are the database's
/// (<c>docs/storage.md</c>, Labels).
/// </summary>
public sealed class IssueLabelConfiguration : IEntityTypeConfiguration<IssueLabel>
{
    public void Configure(EntityTypeBuilder<IssueLabel> builder)
    {
        builder.ToTable("issue_label");

        builder.HasKey(l => new { l.IssueId, l.LabelId }).HasName("pk_issue_label");
        builder.Property(l => l.IssueId).HasColumnName("issue_id");
        builder.Property(l => l.LabelId).HasColumnName("label_id");

        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(l => l.IssueId)
            .HasConstraintName("fk_issue_label_issue")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Label>()
            .WithMany()
            .HasForeignKey(l => l.LabelId)
            .HasConstraintName("fk_issue_label_label")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <inheritdoc cref="IssueLabelConfiguration"/>
public sealed class EpicLabelConfiguration : IEntityTypeConfiguration<EpicLabel>
{
    public void Configure(EntityTypeBuilder<EpicLabel> builder)
    {
        builder.ToTable("epic_label");

        builder.HasKey(l => new { l.EpicId, l.LabelId }).HasName("pk_epic_label");
        builder.Property(l => l.EpicId).HasColumnName("epic_id");
        builder.Property(l => l.LabelId).HasColumnName("label_id");

        builder.HasOne<Epic>()
            .WithMany()
            .HasForeignKey(l => l.EpicId)
            .HasConstraintName("fk_epic_label_epic")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Label>()
            .WithMany()
            .HasForeignKey(l => l.LabelId)
            .HasConstraintName("fk_epic_label_label")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
