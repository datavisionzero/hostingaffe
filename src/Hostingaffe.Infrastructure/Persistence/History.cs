using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The history rows, appended with the write they record and saved with it.</summary>
public sealed class History(HostingaffeDbContext context) : IHistory
{
    /// <summary>
    /// The history across every subject, with the deployments mixed in
    /// (<c>docs/storage.md</c>, The history).
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQL rather than LINQ because three things happen here that the model has
    /// no place for: the rows of one act are folded into one event, the two
    /// sides are ordered against each other, and each event is resolved to the
    /// address of its subject and to the machine it belongs to. Six left joins
    /// by primary key do the last of those, and which one answers is the
    /// subject's kind — the pair carries no foreign key precisely because it
    /// points at more than one table.
    /// </para>
    /// <para>
    /// The cursor is pushed into both halves before anything is joined, so a
    /// walk reads the tail of the table rather than all of it. It cannot cut a
    /// group, because every row of an act carries the same <c>at</c>; and it
    /// cannot cost a deployment its predecessor, because <c>lag</c> looks
    /// backwards and backwards is where the cursor still reads.
    /// </para>
    /// </remarks>
    private const string Statement = """
        with changes as (
            select h.at                                      as at,
                   'history'                                 as source,
                   lpad(max(h.id)::text, 20, '0')            as ident,
                   h.subject                                 as kind,
                   h.subject_id                              as subject_id,
                   h.actor_id                                as actor_id,
                   h.note                                    as note,
                   json_agg(json_build_object(
                       'field', h.field, 'old_value', h.old_value, 'new_value', h.new_value)
                       order by h.id)                        as changes
            from history h
            where (@before_at is null or h.at <= @before_at)
              -- The birth of a deployment is the deployment, which is read
              -- from its own table two branches down. The row the recording
              -- wrote beside it would be that same event a second time; every
              -- other row on a deployment is a correction to one, and those
              -- are changes like any other.
              and not (h.subject = 'deployment' and h.field = 'created')
            group by h.subject, h.subject_id, h.actor_id, h.at, h.note
        ),
        deployments as (
            select d.at                                      as at,
                   'deployment'                              as source,
                   d.id::text                                as ident,
                   'deployment'                              as kind,
                   d.id                                      as subject_id,
                   d.created_by                              as actor_id,
                   nullif(d.note, '')                        as note,
                   json_build_array(json_build_object(
                       'field', 'version',
                       'old_value', lag(d.version) over (
                           partition by d.installation_id order by d.at, d.number),
                       'new_value', d.version))              as changes
            from deployment d
            where d.deleted_at is null
              and (@before_at is null or d.at <= @before_at)
        ),
        events as (
            select * from changes
            union all
            select * from deployments
        )
        select e.at, e.source, e.ident, e.kind, e.actor_id, e.note, e.changes::text,
               case e.kind
                   when 'machine'      then m.key
                   when 'software'     then s.key
                   when 'installation' then i.key
                   when 'file'         then f.path
                   when 'page'         then p.slug
                   when 'deployment'   then di.key
               end                                           as subject,
               case when e.kind = 'deployment' then d.number end as number,
               coalesce(m.key, im.key, fm.key, fim.key, pm.key, pim.key, dim.key) as machine
        from events e
        left join machine      m   on e.kind = 'machine'      and m.id  = e.subject_id
        left join software     s   on e.kind = 'software'     and s.id  = e.subject_id
        left join installation i   on e.kind = 'installation' and i.id  = e.subject_id
        left join machine      im  on im.id  = i.machine_id
        left join file         f   on e.kind = 'file'         and f.id  = e.subject_id
        left join machine      fm  on fm.id  = f.machine_id
        left join installation fi  on fi.id  = f.installation_id
        left join machine      fim on fim.id = fi.machine_id
        left join page         p   on e.kind = 'page'         and p.id  = e.subject_id
        left join machine      pm  on pm.id  = p.machine_id
        left join installation pi  on pi.id  = p.installation_id
        left join machine      pim on pim.id = pi.machine_id
        left join deployment   d   on e.kind = 'deployment'   and d.id  = e.subject_id
        left join installation di  on di.id  = d.installation_id
        left join machine      dim on dim.id = di.machine_id
        where (@kind is null or e.kind = @kind)
          and (@machine is null
               or coalesce(m.key, im.key, fm.key, fim.key, pm.key, pim.key, dim.key) = @machine)
          and (@before_at is null
               or (e.at, e.source, e.ident) < (@before_at, @before_source, @before_ident))
        order by e.at desc, e.source desc, e.ident desc
        limit @limit
        """;

    public void Add(HistoryEntry entry) => context.History.Add(entry);

    public async Task<IReadOnlyList<HistoryEntry>> ListAsync(
        HistorySubject subject, Guid subjectId, CancellationToken cancellationToken) =>
        await context.History
            .Where(h => h.Subject == subject && h.SubjectId == subjectId)
            .OrderBy(h => h.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HistoryEvent>> ReadAsync(
        string? machine,
        HistorySubject? kind,
        HistoryCursor? before,
        int limit,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(Statement, connection);
            if (context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction transaction)
            {
                command.Transaction = transaction;
            }

            // Typed rather than inferred: every one of these is null on the
            // ordinary read, and Npgsql infers no type from a null.
            command.Parameters.Add(new NpgsqlParameter("machine", NpgsqlDbType.Text)
            {
                Value = (object?)machine ?? DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Text)
            {
                Value = kind is { } wanted ? Spelling.Of(wanted) : DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("before_at", NpgsqlDbType.TimestampTz)
            {
                Value = before is { } moment ? moment.At : DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("before_source", NpgsqlDbType.Text)
            {
                Value = before is { } side ? side.Source : DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("before_ident", NpgsqlDbType.Text)
            {
                Value = before is { } row ? row.Ident : DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit });

            var events = new List<HistoryEvent>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var at = reader.GetFieldValue<DateTimeOffset>(0);

                events.Add(new HistoryEvent(
                    at,
                    new HistoryCursor(at, reader.GetString(1), reader.GetString(2)),
                    reader.GetString(3),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetGuid(4),
                    Changes(reader.GetString(6)),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            return events;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// The fields of one act, as the statement aggregated them: in the order
    /// they were written, which is the order the act wrote them in.
    /// </summary>
    private static IReadOnlyList<FieldChange> Changes(string aggregated)
    {
        using var json = JsonDocument.Parse(aggregated);

        return
        [
            .. json.RootElement.EnumerateArray().Select(change => new FieldChange(
                change.GetProperty("field").GetString()!,
                Text(change, "old_value"),
                Text(change, "new_value"))),
        ];
    }

    private static string? Text(JsonElement change, string member) =>
        change.TryGetProperty(member, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.GetString()
            : null;
}
