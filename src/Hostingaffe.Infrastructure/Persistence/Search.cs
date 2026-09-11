using System.Globalization;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// One statement over every searchable surface of the record
/// (<c>docs/storage.md</c>, Searching).
/// </summary>
/// <remarks>
/// <para>
/// It is SQL rather than six LINQ queries because it is one question — "where
/// was that again" — and six round trips answering it in an order the caller
/// then has to reassemble would be the same query written badly. Every part
/// reads a stored <c>tsvector</c> through its GIN index; the <c>where</c> of a
/// hit is a second, unindexed match on the same row, which costs one expression
/// per row that was already found.
/// </para>
/// <para>
/// <strong>Ports are numbers, not words.</strong> <c>18502</c> in a tsvector is
/// a token of the string <c>18502/tcp</c> and would never be found by typing
/// the number, so a query that is a port number is looked up in the column as
/// well. That is what makes VISION 5's own example answer. It asks whether such
/// a row exists rather than joining it, because 53 may be both <c>tcp</c> and
/// <c>udp</c> and that is one installation, not two answers.
/// </para>
/// <para>
/// <strong>Paths are letters, not words.</strong> Postgres makes one token of
/// <c>/srv/caddy/caddy.env</c>, so <c>/srv/caddy</c> matches nothing however
/// often the path occurs. A query that is one word with a slash or a dot in it
/// is therefore looked for as a fragment as well, beside the words and over the
/// same surfaces — through the trigram indexes, and never instead of the
/// <c>tsvector</c> (ADR 0012).
/// </para>
/// <para>
/// <strong>A secret is one hit, and not one per secret.</strong> Its name and
/// the file it lies in are a vector on its own row, and an installation whose
/// own fields already answered is not listed a second time for them: somebody
/// asked where something was, and the same installation twice is not two
/// answers.
/// </para>
/// <para>
/// <strong>A file is searched at the revision it is at.</strong> What an older
/// revision said stopped being true when the next one was written, and a search
/// that answered with it would send somebody to a line that is not there.
/// </para>
/// </remarks>
public sealed class Search(HostingaffeDbContext context) : ISearch
{
    /// <summary>
    /// The shortest fragment worth looking for. A trigram is three characters,
    /// and below that the index cannot narrow anything — a query of two would
    /// read every file of the record to answer.
    /// </summary>
    private const int FragmentMinimumLength = 3;

    private const string Statement = """
        with q as (
            select websearch_to_tsquery('simple', @query) as words,
                   @fragment::text as fragment
        )
        select * from (
            select 1 as ordinal, 'machine' as kind, m.key as key, m.name as name,
                   null::int as number, null::text as directory,
                   null::text as owner_kind, null::text as owner_key,
                   case when to_tsvector('simple', m.description) @@ q.words
                          or (q.fragment is not null and m.description ilike q.fragment)
                        then 'description' else 'fields' end as place
            from machine m, q
            where m.deleted_at is null
              and (m.search @@ q.words or (q.fragment is not null and m.letters ilike q.fragment))

            union all
            select 2, 'software', s.key, s.name, null::int, null::text, null::text, null::text,
                   case when to_tsvector('simple', s.description) @@ q.words
                          or (q.fragment is not null and s.description ilike q.fragment)
                        then 'description' else 'fields' end
            from software s, q
            where s.deleted_at is null
              and (s.search @@ q.words or (q.fragment is not null and s.letters ilike q.fragment))

            union all
            select 3, 'installation', i.key, i.name, null::int, null::text, null::text, null::text,
                   case when to_tsvector('simple', i.description) @@ q.words
                          or (q.fragment is not null and i.description ilike q.fragment)
                        then 'description' else 'fields' end
            from installation i, q
            where i.deleted_at is null
              and (i.search @@ q.words or (q.fragment is not null and i.letters ilike q.fragment))

            union all
            select 3, 'installation', i.key, i.name, null::int, null::text, null::text, null::text, 'ports'
            from installation i
            where i.deleted_at is null and @port is not null
              and exists (select 1 from installation_port p
                          where p.installation_id = i.id and p.port = @port)

            union all
            select 3, 'installation', i.key, i.name, null::int, null::text, null::text, null::text, 'secrets'
            from installation i, q
            where i.deleted_at is null
              and not (i.search @@ q.words or (q.fragment is not null and i.letters ilike q.fragment))
              and exists (select 1 from installation_secret s
                          where s.installation_id = i.id
                            and (s.search @@ q.words
                                 or (q.fragment is not null and s.letters ilike q.fragment)))

            union all
            select 4, 'deployment', i.key, d.version, d.number, null::text, 'installation'::text, i.key,
                   case when to_tsvector('simple', d.note) @@ q.words
                          or (q.fragment is not null and d.note ilike q.fragment)
                        then 'note' else 'fields' end
            from deployment d
            join installation i on i.id = d.installation_id, q
            where d.deleted_at is null and i.deleted_at is null
              and (d.search @@ q.words or (q.fragment is not null and d.letters ilike q.fragment))

            union all
            select 5, 'file', f.path, '', null::int, f.directory,
                   case when f.machine_id is not null then 'machine' else 'installation' end,
                   coalesce(m.key, i.key),
                   case when r.search @@ q.words
                          or (q.fragment is not null and r.content ilike q.fragment)
                        then 'content' else 'path' end
            from file f
            left join machine m on m.id = f.machine_id
            left join installation i on i.id = f.installation_id
            join file_revision r
              on r.file_id = f.id
             and r.revision = (select max(revision) from file_revision where file_id = f.id), q
            where f.deleted_at is null
              and coalesce(m.deleted_at, i.deleted_at) is null
              and (f.search @@ q.words or r.search @@ q.words
                   or (q.fragment is not null
                       and (f.letters ilike q.fragment or r.content ilike q.fragment)))

            union all
            select 6, 'page', p.slug, p.title, null::int, null::text,
                   case when p.machine_id is not null then 'machine'
                        when p.installation_id is not null then 'installation' end,
                   coalesce(m.key, i.key),
                   case when to_tsvector('simple', p.title) @@ q.words
                          or (q.fragment is not null and p.title ilike q.fragment)
                        then 'title' else 'body' end
            from page p
            left join machine m on m.id = p.machine_id
            left join installation i on i.id = p.installation_id, q
            where p.deleted_at is null
              and (p.search @@ q.words
                   or (q.fragment is not null
                       and (p.title ilike q.fragment or p.body ilike q.fragment)))
        ) hits
        order by ordinal, key, number
        limit @limit
        """;

    public async Task<IReadOnlyList<SearchHit>> FindAsync(
        string query, int limit, CancellationToken cancellationToken)
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

            // Typed rather than inferred: a null port has no value to infer a
            // type from, and Npgsql will not guess one.
            command.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query });
            command.Parameters.Add(new NpgsqlParameter("port", NpgsqlDbType.Integer)
            {
                Value = Port(query) is { } port ? port : DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("fragment", NpgsqlDbType.Text)
            {
                Value = Fragment(query) is { } fragment ? fragment : DBNull.Value,
            });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit });

            var hits = new List<SearchHit>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add(new SearchHit(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    Owner(reader),
                    reader.GetString(8)));
            }

            return hits;
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
    /// The query as a port number, where it is one. Anything outside the range
    /// a port has is not a port, whatever it parses as.
    /// </summary>
    private static int? Port(string query) =>
        int.TryParse(query.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= 65535
            ? number
            : null;

    /// <summary>
    /// The query as a <c>like</c> pattern, where it looks like a path: one word
    /// — no spaces, the way a path is typed — with a slash or a dot in it, and
    /// long enough for a trigram.
    /// </summary>
    /// <remarks>
    /// The whole query or nothing, like the port: a search for
    /// <c>caddy /srv/caddy</c> is two words, and two words are what the
    /// <c>tsquery</c> is for. The wildcards a fragment may itself contain are
    /// escaped here, so that a path with an underscore in it means that
    /// underscore.
    /// </remarks>
    private static string? Fragment(string query)
    {
        var word = query.Trim();

        return word.Length >= FragmentMinimumLength
            && !word.Any(char.IsWhiteSpace)
            && (word.Contains('/') || word.Contains('.'))
            ? $"%{word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%"
            : null;
    }

    private static Anchor? Owner(NpgsqlDataReader reader) =>
        reader.IsDBNull(6) || reader.IsDBNull(7)
            ? null
            : new Anchor(Spelling.Read<AnchorKind>(reader.GetString(6), "owner")!.Value, Guid.Empty, reader.GetString(7));
}
