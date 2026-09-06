using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

public sealed class ProjectAccessConfiguration : IEntityTypeConfiguration<ProjectAccess>
{
    public void Configure(EntityTypeBuilder<ProjectAccess> builder)
    {
        builder.ToTable("project_access");
        builder.HasKey(access => new { access.ProjectId, access.UserId }).HasName("pk_project_access");
        builder.HasIndex(access => access.UserId).HasDatabaseName("project_access_user");
        builder.Property(access => access.ProjectId).HasColumnName("project_id");
        builder.Property(access => access.UserId).HasColumnName("user_id");
        builder.Property(access => access.GrantedBy).HasColumnName("granted_by");
        builder.Property(access => access.GrantedAt).HasColumnName("granted_at");
        builder.HasOne<Project>().WithMany().HasForeignKey(access => access.ProjectId)
            .HasConstraintName("fk_project_access_project").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(access => access.UserId)
            .HasConstraintName("fk_project_access_user").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(access => access.GrantedBy)
            .HasConstraintName("fk_project_access_granted_by").OnDelete(DeleteBehavior.NoAction);
    }
}
