namespace Hostingaffe.Domain.Installations;

/// <summary>
/// One installation another installation needs — the third relationship of the
/// model, and the one VISION 15.2 held back until a real host asked for it
/// (ADR 0014).
/// </summary>
/// <remarks>
/// <para>
/// It carries the id and nothing else. The key is what a person reads, and it
/// is resolved where a shape or a document is assembled, the way the machine
/// and the software of an installation are: a key repeated in this row would be
/// a second place to read it from, and the row would have to argue why it can
/// never disagree with the first.
/// </para>
/// <para>
/// <strong>There is no row the other way.</strong> What needs this installation
/// is the same rows read from the other side — <c>needed_by</c>, derived on
/// read the way a version is derived from the deployments, and never written.
/// </para>
/// </remarks>
public sealed class Dependency
{
    private Dependency()
    {
        // EF Core materializes through this; every other route goes through On.
    }

    private Dependency(Guid dependsOnId) => DependsOnId = dependsOnId;

    /// <summary>The installation that is depended on.</summary>
    public Guid DependsOnId { get; private init; }

    /// <exception cref="ArgumentException">The id is empty, which names nothing.</exception>
    public static Dependency On(Guid dependsOnId) =>
        dependsOnId == Guid.Empty
            ? throw new ArgumentException("A dependency names an installation.", nameof(dependsOnId))
            : new Dependency(dependsOnId);
}
