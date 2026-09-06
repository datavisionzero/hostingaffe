using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Releases;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

public sealed class ReleaseIssueConfiguration : IEntityTypeConfiguration<ReleaseIssue>
{
    public void Configure(EntityTypeBuilder<ReleaseIssue> builder)
    {
        builder.ToTable("release_issue");
        builder.HasKey(x => new { x.ReleaseId, x.IssueId }).HasName("pk_release_issue");
        builder.Property(x => x.ReleaseId).HasColumnName("release_id");
        builder.Property(x => x.IssueId).HasColumnName("issue_id");
        builder.HasOne<Release>().WithMany().HasForeignKey(x => x.ReleaseId).HasConstraintName("fk_release_issue_release").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Issue>().WithMany().HasForeignKey(x => x.IssueId).HasConstraintName("fk_release_issue_issue").OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.IssueId).HasDatabaseName("release_issue_issue");
    }
}
