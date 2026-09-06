using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The issue row (<c>docs/storage.md</c>, Issues), with its three invariants as
/// check constraints and the claim's four columns as an owned <see cref="Claim"/>.
/// </summary>
/// <remarks>
/// The <c>issue_read</c> view over this table is not in the model — EF Core
/// does not create views — and is SQL in the migration that created the table.
/// Every read of an issue goes through it; a migration that changes a column
/// the view names has to drop and recreate it, and Postgres will say so.
/// </remarks>
public sealed class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public const string ReadView = "issue_read";

    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("issue", table =>
        {
            table.HasCheckConstraint(
                "ck_issue_status",
                "status in ('backlog', 'todo', 'in_progress', 'review', 'done', 'canceled')");
            table.HasCheckConstraint("ck_issue_priority", "priority between 0 and 4");
            table.HasCheckConstraint("ck_issue_parent_not_self", "parent_id is distinct from id");

            // An issue is in_progress exactly when somebody holds a claim on it
            // (VISION 11): claiming sets the status, releasing clears it, one
            // step not two.
            table.HasCheckConstraint("ck_issue_claimed", "(status = 'in_progress') = (claimed_by is not null)");

            // Closed exactly when closed_at is set (CONTEXT.md, Closed).
            table.HasCheckConstraint(
                "ck_issue_closed",
                "(status in ('done', 'canceled')) = (closed_at is not null)");

            // The claim's columns come and go together; only its expiry may be
            // null on its own, for a user's claim.
            table.HasCheckConstraint(
                "ck_issue_claim_columns",
                "(claimed_by is null) = (claimed_at is null) and (claimed_by is null) = (claim_extended_at is null)");
        });

        builder.HasKey(i => i.Id).HasName("pk_issue");
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.ProjectId).HasColumnName("project_id").IsRequired();
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(i => i.ProjectId)
            .HasConstraintName("fk_issue_project")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(i => i.Number).HasColumnName("number").IsRequired();
        builder.HasIndex(i => new { i.ProjectId, i.Number }).IsUnique().HasDatabaseName("issue_number");

        builder.Property(i => i.Title).HasColumnName("title").IsRequired();

        builder.Property(i => i.Description)
            .HasColumnName("description")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(i => i.Result).HasColumnName("result");

        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql("to_tsvector('simple', title || ' ' || description || ' ' || coalesce(result, ''))", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("issue_search");

        // The sentinel says which value means "not set, take the default":
        // `todo`, the birth status. Without it EF Core would take the enum's
        // zero — `backlog` — for unset and an issue parked from birth would be
        // born in `todo`.
        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion(new SnakeCaseEnumConverter<IssueStatus>())
            .HasDefaultValue(IssueStatus.Todo)
            .HasSentinel(IssueStatus.Todo)
            .IsRequired();

        builder.Property(i => i.Ready).HasColumnName("ready").HasDefaultValue(false).IsRequired();

        builder.Property(i => i.Priority)
            .HasColumnName("priority")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .HasDefaultValue(Priority.None)
            .IsRequired();

        builder.Property(i => i.AssigneeId).HasColumnName("assignee_id");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(i => i.AssigneeId)
            .HasConstraintName("fk_issue_assignee")
            .OnDelete(DeleteBehavior.NoAction);

        // No cascade: an epic with issues is not deletable, so the reference
        // never dangles.
        builder.Property(i => i.EpicId).HasColumnName("epic_id");
        builder.HasOne<Epic>()
            .WithMany()
            .HasForeignKey(i => i.EpicId)
            .HasConstraintName("fk_issue_epic")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(i => i.ParentId).HasColumnName("parent_id");
        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(i => i.ParentId)
            .HasConstraintName("fk_issue_parent")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(i => i.ParentId)
            .HasFilter("parent_id is not null")
            .HasDatabaseName("issue_parent");

        // Four columns on this row, present together or absent together — an
        // optional dependent sharing the table, and null when nobody holds it.
        builder.OwnsOne(i => i.Claim, claim =>
        {
            claim.Property(c => c.HolderId).HasColumnName("claimed_by");
            claim.Property(c => c.ClaimedAt).HasColumnName("claimed_at");
            claim.Property(c => c.ExtendedAt).HasColumnName("claim_extended_at");
            claim.Property(c => c.ExpiresAt).HasColumnName("claim_expires_at");

            claim.HasOne<Identity>()
                .WithMany()
                .HasForeignKey(c => c.HolderId)
                .HasConstraintName("fk_issue_claimed_by")
                .OnDelete(DeleteBehavior.NoAction);

            claim.HasIndex(c => c.HolderId)
                .HasFilter("claimed_by is not null")
                .HasDatabaseName("issue_claim");
        });

        builder.Property(i => i.AuthorId).HasColumnName("author_id").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(i => i.AuthorId)
            .HasConstraintName("fk_issue_author")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(i => i.ClosedAt).HasColumnName("closed_at");
        builder.Property(i => i.DeletedAt).HasColumnName("deleted_at");

        builder.Property(i => i.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(i => i.DeletedBy)
            .HasConstraintName("fk_issue_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        // The order `next` hands issues out in, over the live rows only:
        // priority first, then the older issue. The epic tie-breaker is a
        // predicate over other issues, not an index.
        builder.HasIndex(i => new { i.ProjectId, i.Priority, i.CreatedAt, i.Number })
            .IsDescending(false, true, false, false)
            .HasFilter("deleted_at is null")
            .HasDatabaseName("issue_next");

        // The default order of a list, and the cursor it pages by.
        builder.HasIndex(i => new { i.ProjectId, i.UpdatedAt, i.Id })
            .IsDescending(false, true, false)
            .HasFilter("deleted_at is null")
            .HasDatabaseName("issue_updated");

        // Epic progress, counted at read time from the live rows.
        builder.HasIndex(i => i.EpicId)
            .HasFilter("deleted_at is null")
            .HasDatabaseName("issue_epic");

        builder.HasIndex(i => i.AssigneeId)
            .HasFilter("assignee_id is not null")
            .HasDatabaseName("issue_assignee");

        builder.Ignore(i => i.Closed);
        builder.Ignore(i => i.Deleted);
    }
}
