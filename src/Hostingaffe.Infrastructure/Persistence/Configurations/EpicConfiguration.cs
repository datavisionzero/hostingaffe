using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class EpicConfiguration : IEntityTypeConfiguration<Epic>
{
    public void Configure(EntityTypeBuilder<Epic> builder)
    {
        builder.ToTable("epic", table =>
        {
            table.HasCheckConstraint("ck_epic_status", "status in ('open', 'closed')");
            table.HasCheckConstraint("ck_epic_closed", "(status = 'closed') = (closed_at is not null)");
        });

        builder.HasKey(e => e.Id).HasName("pk_epic");
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .HasConstraintName("fk_epic_project")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.Number).HasColumnName("number").IsRequired();
        builder.HasIndex(e => new { e.ProjectId, e.Number }).IsUnique().HasDatabaseName("epic_number");

        builder.Property(e => e.Title).HasColumnName("title").IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion(new SnakeCaseEnumConverter<EpicStatus>())
            .HasDefaultValue(EpicStatus.Open)
            .IsRequired();

        builder.Property(e => e.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("fk_epic_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.ClosedAt).HasColumnName("closed_at");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        builder.Property(e => e.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("fk_epic_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(e => e.Closed);
        builder.Ignore(e => e.Deleted);
    }
}
