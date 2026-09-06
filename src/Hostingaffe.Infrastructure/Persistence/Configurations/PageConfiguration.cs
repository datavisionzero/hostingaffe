using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Pages;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The instance's flat wiki (<c>docs/storage.md</c>, Pages). The unique index
/// covers deleted rows on purpose: a slug stays spent until the purge, so that
/// a restore never lands on a name somebody else has taken (ADR 0013).
/// </summary>
public sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable("page");

        builder.HasKey(p => p.Id).HasName("pk_page");
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Slug).HasColumnName("slug").IsRequired();

        // One index for both jobs: it holds the slug unique and it is the order
        // a flat wiki is listed in, which is the only order it has.
        builder.HasIndex(p => p.Slug).IsUnique().HasDatabaseName("page_slug");

        builder.Property(p => p.Title).HasColumnName("title").IsRequired();

        builder.Property(p => p.Body)
            .HasColumnName("body")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        // The wiki is flat because the search replaces the navigation a tree
        // would have been (VISION 7), so the search has to know it.
        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql("to_tsvector('simple', title || ' ' || body)", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("page_search");

        builder.Property(p => p.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(p => p.CreatedBy)
            .HasConstraintName("fk_page_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(p => p.UpdatedBy)
            .HasConstraintName("fk_page_updated_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.Property(p => p.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(p => p.DeletedBy)
            .HasConstraintName("fk_page_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(p => p.Deleted);
    }
}
