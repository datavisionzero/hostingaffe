namespace Hostingaffe.Application.Ports;

/// <summary>
/// One transaction around several port calls — a guarded <c>PATCH</c>, a write
/// that also appends to the history. The stores share the unit of work, so what
/// is added inside is committed together or not at all.
/// </summary>
public interface ITransactions
{
    /// <summary>
    /// Runs <paramref name="work"/> in one transaction. An act inside an act
    /// joins the one it is already in: the bulk write is one act made of the
    /// ordinary ones, and the outermost call is what commits or rolls back.
    /// </summary>
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);
}
