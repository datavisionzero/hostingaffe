using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;
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
        builder.ToTable("page", table =>
        {
            table.HasCheckConstraint("ck_page_kind", "kind in ('runbook', 'decision', 'note')");

            // Attached to one thing, or to nothing at all — and the third
            // state, both, is not one the model has.
            table.HasCheckConstraint("ck_page_attached_to", "num_nonnulls(machine_id, installation_id) <= 1");
        });

        builder.HasKey(p => p.Id).HasName("pk_page");
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Slug).HasColumnName("slug").IsRequired();

        // One index for both jobs: it holds the slug unique and it is the order
        // a flat wiki is listed in, which is the only order it has.
        builder.HasIndex(p => p.Slug).IsUnique().HasDatabaseName("page_slug");

        builder.Property(p => p.Title).HasColumnName("title").IsRequired();

        // No database default. `runbook` is the first value of the enum and so
        // the CLR default, and a column default would make EF leave it out of
        // every insert that meant it — the row would come back a `note`. What
        // the pages written before the column existed became is the migration's
        // business, and it is a one-line backfill there.
        builder.Property(p => p.Kind)
            .HasColumnName("kind")
            .HasConversion(new SnakeCaseEnumConverter<PageKind>())
            .IsRequired();

        // What the page hangs on, or nothing — then it is the instance's. No
        // cascade either way: a page does not follow its anchor into deletion.
        builder.Property(p => p.MachineId).HasColumnName("machine_id");
        builder.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(p => p.MachineId)
            .HasConstraintName("fk_page_machine")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(p => p.InstallationId).HasColumnName("installation_id");
        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(p => p.InstallationId)
            .HasConstraintName("fk_page_installation")
            .OnDelete(DeleteBehavior.NoAction);

        // Read by whenever the pages of a machine or an installation are asked
        // for, which is what makes `attached_to` a filter rather than a label.
        builder.HasIndex(p => p.MachineId).HasDatabaseName("page_on_machine").HasFilter("machine_id is not null");
        builder.HasIndex(p => p.InstallationId).HasDatabaseName("page_on_installation").HasFilter("installation_id is not null");

        builder.Ignore(p => p.Attached);

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

        // And it has to know the letters as well, because a runbook is where
        // the paths are written out (ADR 0012). The title and the body carry
        // their own index rather than one over both: the hit already has to say
        // which of the two answered, and a column that is a text needs no copy
        // of itself to be searched by fragment.
        builder.HasIndex(p => p.Title).HasMethod("GIN").HasOperators("gin_trgm_ops").HasDatabaseName("page_title_letters");
        builder.HasIndex(p => p.Body).HasMethod("GIN").HasOperators("gin_trgm_ops").HasDatabaseName("page_body_letters");

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
