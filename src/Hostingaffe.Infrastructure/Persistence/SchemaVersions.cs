namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// One question: are there migrations in this database that this binary does
/// not know about?
/// </summary>
/// <remarks>
/// The identifiers are EF Core's migration ids, which begin with the timestamp
/// they were scaffolded at, so "newer" would be an ordinal comparison — but
/// nothing here relies on it. What is refused is anything <em>unknown</em>,
/// which is the honest reading: a database carrying a migration from a branch
/// that never merged is not newer either, and it is just as much not ours to
/// serve.
/// </remarks>
public static class SchemaVersions
{
    /// <summary>
    /// The migration ids among <paramref name="applied"/> that are not among
    /// <paramref name="known"/>, in order.
    /// </summary>
    public static IReadOnlyList<string> NotKnownHere(IEnumerable<string> applied, IEnumerable<string> known) =>
        [.. applied.Except(known, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}

/// <summary>
/// What is thrown when the schema in front of this binary was written by a
/// newer one.
/// </summary>
/// <remarks>
/// Migrations only run forward and there is no downgrade path (ADR 0011) —
/// going back a version means restoring the backup taken before the upgrade —
/// which is what makes this refusal load-bearing rather than tidy: it is the
/// thing standing between a mistaken <c>docker compose up</c> on a stale image
/// and an instance quietly serving reads and writes against a shape it
/// misunderstands.
/// </remarks>
public sealed class SchemaIsNewerException(IReadOnlyList<string> migrations) : Exception(Describe(migrations))
{
    /// <summary>The migration ids this binary does not know about.</summary>
    public IReadOnlyList<string> Migrations { get; } = migrations;

    private static string Describe(IReadOnlyList<string> migrations) =>
        $"This database was migrated by a newer hostingaffe. It carries "
        + $"{migrations.Count} migration(s) this version does not know about: "
        + $"{string.Join(", ", migrations)}. Start the version that migrated it, "
        + "or restore the backup taken before the upgrade — there is no downgrade path.";
}
