namespace Hostingaffe.Application.Ports;

/// <summary>
/// One transaction around several port calls — a guarded <c>PATCH</c>, a write
/// that also appends to the history. The stores share the unit of work, so what
/// is added inside is committed together or not at all.
/// </summary>
public interface ITransactions
{
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);
}
