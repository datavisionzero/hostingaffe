using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IssueRead"/>
public sealed class IssueReadConfiguration : IEntityTypeConfiguration<IssueRead>
{
    public void Configure(EntityTypeBuilder<IssueRead> builder)
    {
        builder.HasNoKey();
        builder.ToView(IssueConfiguration.ReadView);

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.ProjectId).HasColumnName("project_id");
        builder.Property(i => i.Number).HasColumnName("number");
        builder.Property(i => i.Title).HasColumnName("title");
        builder.Property(i => i.Description).HasColumnName("description");
        builder.Property(i => i.Result).HasColumnName("result");
        builder.Property(i => i.Status).HasColumnName("status").HasConversion(new SnakeCaseEnumConverter<IssueStatus>());
        builder.Property(i => i.Ready).HasColumnName("ready");
        builder.Property(i => i.Priority).HasColumnName("priority").HasColumnType("smallint").HasConversion<short>();
        builder.Property(i => i.AssigneeId).HasColumnName("assignee_id");
        builder.Property(i => i.EpicId).HasColumnName("epic_id");
        builder.Property(i => i.ParentId).HasColumnName("parent_id");
        builder.Property(i => i.ClaimedBy).HasColumnName("claimed_by");
        builder.Property(i => i.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(i => i.ClaimExpiresAt).HasColumnName("claim_expires_at");
        builder.Property(i => i.AuthorId).HasColumnName("author_id");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Property(i => i.ClosedAt).HasColumnName("closed_at");
    }
}
