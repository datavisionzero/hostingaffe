using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;

using File = Hostingaffe.Domain.Files.File;
using FileDirectory = Hostingaffe.Domain.Files.FileDirectory;
using FilePath = Hostingaffe.Domain.Files.FilePath;
using NpgsqlTypes;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The files and their revisions (<c>docs/storage.md</c>, Files). A file belongs
/// to exactly one machine or exactly one installation, and the check constraint
/// is what says "exactly".
/// </summary>
/// <remarks>
/// Nothing here holds the current content, the mode bit or the revision number:
/// they are the newest revision's, computed on read. A column repeating them
/// would be the second truth a write has to remember to refresh.
/// </remarks>
public sealed class FileConfiguration : IEntityTypeConfiguration<File>
{
    public void Configure(EntityTypeBuilder<File> builder)
    {
        builder.ToTable("file", table =>
        {
            table.HasCheckConstraint("ck_file_owner", "num_nonnulls(machine_id, installation_id) = 1");

            // Only a machine's file says where on the machine it lies. An
            // installation's directory is the installation's own path, once for
            // all of its files, and a second answer here would be the one that
            // drifts (ADR 0008).
            table.HasCheckConstraint("ck_file_directory", "directory is null or machine_id is not null");
        });

        builder.HasKey(f => f.Id).HasName("pk_file");
        builder.Property(f => f.Id).HasColumnName("id");

        builder.Property(f => f.MachineId).HasColumnName("machine_id");
        builder.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(f => f.MachineId)
            .HasConstraintName("fk_file_machine")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(f => f.InstallationId).HasColumnName("installation_id");
        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(f => f.InstallationId)
            .HasConstraintName("fk_file_installation")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(f => f.Path).HasColumnName("path").HasMaxLength(FilePath.MaxLength).IsRequired();

        builder.Property(f => f.Directory).HasColumnName("directory").HasMaxLength(FileDirectory.MaxLength);

        // One index per owner kind, each unique and each the order the files of
        // an owner are read in. They cover deleted rows on purpose, so a restore
        // never lands on a path somebody else has taken (ADR 0013).
        builder.HasIndex(f => new { f.MachineId, f.Path })
            .IsUnique()
            .HasFilter("machine_id is not null")
            .HasDatabaseName("file_on_machine");

        builder.HasIndex(f => new { f.InstallationId, f.Path })
            .IsUnique()
            .HasFilter("installation_id is not null")
            .HasDatabaseName("file_on_installation");

        builder.OwnsMany(f => f.Revisions, revision =>
        {
            revision.ToTable("file_revision");

            revision.WithOwner().HasForeignKey("file_id").HasConstraintName("fk_file_revision_file");
            revision.Property(r => r.Number).HasColumnName("revision");
            revision.Property(r => r.Content).HasColumnName("content").IsRequired();
            revision.Property(r => r.Executable).HasColumnName("executable").IsRequired();

            // Every revision is searchable, and the search reads only the
            // current one: what an old revision said stopped being true when
            // the next one was written (docs/storage.md, Searching).
            revision.Property<NpgsqlTsVector>("Search")
                .HasColumnName("search")
                .HasComputedColumnSql("to_tsvector('simple', content)", stored: true);
            revision.HasIndex("Search").HasMethod("GIN").HasDatabaseName("file_revision_search");

            revision.Property(r => r.By).HasColumnName("by").IsRequired();
            revision.HasOne<Identity>()
                .WithMany()
                .HasForeignKey(r => r.By)
                .HasConstraintName("fk_file_revision_by")
                .OnDelete(DeleteBehavior.NoAction);

            revision.Property(r => r.At).HasColumnName("at").IsRequired();

            revision.HasKey("file_id", nameof(Domain.Files.FileRevision.Number)).HasName("pk_file_revision");
        });

        // The revisions are what the file is: what it says now is the newest of
        // them, so a read without them is a file without a content.
        builder.Navigation(f => f.Revisions).AutoInclude();

        // The path is the file's; what it says is the revision's, below.
        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql("to_tsvector('simple', path)", stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("file_search");

        builder.Property(f => f.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(f => f.CreatedBy)
            .HasConstraintName("fk_file_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(f => f.DeletedAt).HasColumnName("deleted_at");

        builder.Property(f => f.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(f => f.DeletedBy)
            .HasConstraintName("fk_file_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(f => f.Deleted);
        builder.Ignore(f => f.Current);
        builder.Ignore(f => f.Revision);
        builder.Ignore(f => f.Content);
        builder.Ignore(f => f.Executable);
        builder.Ignore(f => f.UpdatedBy);
        builder.Ignore(f => f.UpdatedAt);
    }
}
