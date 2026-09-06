using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comment");

        builder.HasKey(c => c.Id).HasName("pk_comment");
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.IssueId).HasColumnName("issue_id").IsRequired();
        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(c => c.IssueId)
            .HasConstraintName("fk_comment_issue")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.AuthorId).HasColumnName("author_id").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .HasConstraintName("fk_comment_author")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(c => c.Body).HasColumnName("body").IsRequired();
        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql("to_tsvector('simple', body)", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("comment_search");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        // Null while the comment is as it was written (ADR 0022). The search
        // column above is generated and stored, so a rewrite recomputes it and
        // a delete takes it with the row — neither needs a hand here.
        builder.Property(c => c.EditedAt).HasColumnName("edited_at");

        builder.HasIndex(c => new { c.IssueId, c.CreatedAt }).HasDatabaseName("comment_issue");
    }
}

/// <summary>
/// A question is open while <c>answer</c> is null, and the partial index is
/// what condition 4 of VISION 10 and the "needs you" list read.
/// </summary>
public sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable("question", table =>
            table.HasCheckConstraint(
                "ck_question_answer",
                "(answer is null) = (answered_by is null) and (answer is null) = (answered_at is null)"));

        builder.HasKey(q => q.Id).HasName("pk_question");
        builder.Property(q => q.Id).HasColumnName("id");

        // Kept beside issue_id so a notification trigger never has to join.
        builder.Property(q => q.ProjectId).HasColumnName("project_id").IsRequired();
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(q => q.ProjectId)
            .HasConstraintName("fk_question_project")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(q => q.IssueId).HasColumnName("issue_id").IsRequired();
        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(q => q.IssueId)
            .HasConstraintName("fk_question_issue")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(q => q.Text).HasColumnName("question").IsRequired();

        builder.Property(q => q.AskedBy).HasColumnName("asked_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(q => q.AskedBy)
            .HasConstraintName("fk_question_asked_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(q => q.AskedAt).HasColumnName("asked_at").IsRequired();

        builder.Property(q => q.Answer).HasColumnName("answer");

        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql("to_tsvector('simple', question || ' ' || coalesce(answer, ''))", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("question_search");

        builder.Property(q => q.AnsweredBy).HasColumnName("answered_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(q => q.AnsweredBy)
            .HasConstraintName("fk_question_answered_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(q => q.AnsweredAt).HasColumnName("answered_at");

        builder.HasIndex(q => new { q.IssueId, q.AskedAt }).HasDatabaseName("question_issue");
        builder.HasIndex(q => q.IssueId).HasFilter("answer is null").HasDatabaseName("question_open");

        builder.Ignore(q => q.Open);
    }
}
