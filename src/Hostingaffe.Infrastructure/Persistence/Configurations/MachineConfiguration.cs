using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Machines;
using NpgsqlTypes;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The machines (<c>docs/storage.md</c>, Machines). The unique index on the key
/// covers deleted rows on purpose: a key stays spent until the purge, so that a
/// restore never lands on a name somebody else has taken (ADR 0013).
/// </summary>
public sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.ToTable("machine", table =>
        {
            // Closed means closed here too: a constraint that lists the words
            // can be read, and it is what keeps a value the code never wrote
            // out of the column (VISION 7).
            table.HasCheckConstraint("ck_machine_kind", "kind in ('vps', 'dedicated', 'vm', 'local')");
            table.HasCheckConstraint("ck_machine_arch", "arch is null or arch in ('amd64', 'arm64')");
            table.HasCheckConstraint("ck_machine_status", "status in ('planned', 'active', 'retired')");

            // Only a vm runs on a machine, and no machine runs on itself. That a
            // longer chain does not close is the write path's — the database
            // cannot see it without walking.
            table.HasCheckConstraint("ck_machine_host", "host_id is null and kind <> 'vm' or kind = 'vm'");
            table.HasCheckConstraint("ck_machine_not_its_own_host", "host_id is null or host_id <> id");
        });

        builder.HasKey(m => m.Id).HasName("pk_machine");
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.Key).HasColumnName("key").HasMaxLength(Key.MaxLength).IsRequired();

        // One index for both jobs: it holds the key unique and it is the order
        // the list is read in.
        builder.HasIndex(m => m.Key).IsUnique().HasDatabaseName("machine_key");

        builder.Property(m => m.Name).HasColumnName("name").HasMaxLength(Machine.NameMaxLength).IsRequired();
        builder.Property(m => m.Hostname).HasColumnName("hostname").HasMaxLength(Machine.FactMaxLength);

        builder.Property(m => m.Kind)
            .HasColumnName("kind")
            .HasConversion(new SnakeCaseEnumConverter<MachineKind>())
            .IsRequired();

        // The one relationship a machine has, and it points at the same table.
        // No cascade: what a deleted machine does to the VMs on it is the soft
        // delete's business, not the database's.
        builder.Property(m => m.HostId).HasColumnName("host_id");
        builder.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(m => m.HostId)
            .HasConstraintName("fk_machine_host")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(m => m.HostId).HasDatabaseName("machine_host");

        builder.Property(m => m.Provider).HasColumnName("provider").HasMaxLength(Machine.FactMaxLength);
        builder.Property(m => m.Plan).HasColumnName("plan").HasMaxLength(Machine.FactMaxLength);
        builder.Property(m => m.Location).HasColumnName("location").HasMaxLength(Machine.FactMaxLength);
        builder.Property(m => m.Os).HasColumnName("os").HasMaxLength(Machine.FactMaxLength);

        builder.Property(m => m.Arch)
            .HasColumnName("arch")
            .HasConversion(new SnakeCaseEnumConverter<Arch>());

        builder.Property(m => m.Cpu).HasColumnName("cpu").HasMaxLength(Machine.FactMaxLength);
        builder.Property(m => m.Memory).HasColumnName("memory").HasMaxLength(Machine.FactMaxLength);
        builder.Property(m => m.Disk).HasColumnName("disk").HasMaxLength(Machine.FactMaxLength);

        builder.Property(m => m.Ipv4).HasColumnName("ipv4").HasMaxLength(Machine.FactMaxLength);
        builder.Property(m => m.Ipv6).HasColumnName("ipv6").HasMaxLength(Machine.FactMaxLength);
        builder.Property(m => m.PrivateIp).HasColumnName("private_ip").HasMaxLength(Machine.FactMaxLength);
        builder.Property(m => m.Ssh).HasColumnName("ssh").HasMaxLength(Machine.FactMaxLength);

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasConversion(new SnakeCaseEnumConverter<Status>())
            .IsRequired();

        builder.Property(m => m.MeasuredAt).HasColumnName("measured_at");

        builder.Property(m => m.Description)
            .HasColumnName("description")
            .HasDefaultValue(string.Empty)
            .IsRequired();

        // What `ha search` reads (docs/storage.md, Searching). One
        // `simple` configuration everywhere, and the closed sets left out:
        // `status=retired` is a filter on the list, not something to find by
        // typing the word.
        builder.Property<NpgsqlTsVector>("Search")
            .HasColumnName("search")
            .HasComputedColumnSql(
                "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(hostname, '') || ' ' "
                + "|| coalesce(provider, '') || ' ' || coalesce(plan, '') || ' ' || coalesce(location, '') || ' ' "
                + "|| coalesce(os, '') || ' ' || coalesce(cpu, '') || ' ' || coalesce(memory, '') || ' ' "
                + "|| coalesce(disk, '') || ' ' || coalesce(ipv4, '') || ' ' || coalesce(ipv6, '') || ' ' "
                + "|| coalesce(private_ip, '') || ' ' || coalesce(ssh, '') || ' ' || description)",
                stored: true);
        builder.HasIndex("Search").HasMethod("GIN").HasDatabaseName("machine_search");

        builder.Property(m => m.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(m => m.CreatedBy)
            .HasConstraintName("fk_machine_created_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(m => m.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(m => m.UpdatedBy)
            .HasConstraintName("fk_machine_updated_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(m => m.DeletedAt).HasColumnName("deleted_at");

        builder.Property(m => m.DeletedBy).HasColumnName("deleted_by");
        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(m => m.DeletedBy)
            .HasConstraintName("fk_machine_deleted_by")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(m => m.Deleted);
    }
}
