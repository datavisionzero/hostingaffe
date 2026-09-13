using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Reports;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <summary>
/// The reports (<c>docs/storage.md</c>, Reports): what a machine said about
/// itself, at a moment.
/// </summary>
/// <remarks>
/// <para>
/// A report has no key. It is numbered per machine, and <c>report_number</c> is
/// what makes that number an address. There is no <c>updated_at</c> and no
/// <c>deleted_at</c>, because a report is never edited and is reached only
/// through its machine (ADR 0015).
/// </para>
/// <para>
/// The body is <c>jsonb</c> because its sections are nested and optional and
/// nothing is ever filtered by one of them: it is read whole. That it is
/// <c>jsonb</c> does not mean any JSON is taken — the shape is the Domain's and
/// is checked at the door.
/// </para>
/// </remarks>
public sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    /// <summary>
    /// How the body is written down. Snake case and nothing null, so that the
    /// column reads as the contract spells it and an absent section is absent
    /// rather than present and empty.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("machine_report", table =>
            table.HasCheckConstraint("ck_machine_report_number", "number >= 1"));

        builder.HasKey(r => r.Id).HasName("pk_machine_report");
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.MachineId).HasColumnName("machine_id").IsRequired();
        builder.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(r => r.MachineId)
            .HasConstraintName("fk_machine_report_machine")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(r => r.Number).HasColumnName("number").IsRequired();

        // The number is the address, and it is one per machine.
        builder.HasIndex(r => new { r.MachineId, r.Number })
            .IsUnique()
            .HasDatabaseName("machine_report_number");

        // Every read is either the latest report of a machine or its latest few,
        // and the sweep goes by `received_at` as well (docs/storage.md).
        builder.HasIndex(r => new { r.MachineId, r.ReceivedAt })
            .IsDescending(false, true)
            .HasDatabaseName("machine_report_when");

        builder.Property(r => r.CollectedAt).HasColumnName("collected_at").IsRequired();
        builder.Property(r => r.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(r => r.Agent).HasColumnName("agent").HasMaxLength(Report.AgentMaxLength);

        builder.Property(r => r.Body)
            .HasColumnName("body")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                new ValueConverter<ReportBody, string>(
                    body => JsonSerializer.Serialize(body, Json),
                    text => JsonSerializer.Deserialize<ReportBody>(text, Json)!),
                new ValueComparer<ReportBody>(
                    (left, right) => left == right,
                    body => body.GetHashCode(),
                    body => body));
    }
}
