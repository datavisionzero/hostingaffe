using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Projects;
using Hostingaffe.Domain.Releases;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <remarks>The case-insensitive <c>release_name</c> expression index is SQL in the migration; EF Core cannot model it.</remarks>
public sealed class ReleaseConfiguration : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.ToTable("release", table => table.HasCheckConstraint("ck_release_published",
            "(status = 'published') = (name is not null) and (status = 'published') = (published_at is not null) and (status = 'published') = (published_by is not null)"));
        builder.HasKey(r => r.Id).HasName("pk_release");
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.ProjectId).HasColumnName("project_id").IsRequired();
        builder.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).HasConstraintName("fk_release_project").OnDelete(DeleteBehavior.Cascade);
        builder.Property(r => r.Name).HasColumnName("name");
        builder.Property(r => r.Description).HasColumnName("description").HasDefaultValue(string.Empty).IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion(new SnakeCaseEnumConverter<ReleaseStatus>()).HasDefaultValue(ReleaseStatus.Open).IsRequired();
        builder.Property(r => r.PublishedAt).HasColumnName("published_at");
        builder.Property(r => r.PublishedBy).HasColumnName("published_by");
        builder.HasOne<Identity>().WithMany().HasForeignKey(r => r.PublishedBy).HasConstraintName("fk_release_published_by").OnDelete(DeleteBehavior.NoAction);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(r => r.ProjectId).IsUnique().HasFilter("status = 'open'").HasDatabaseName("release_open");
    }
}
