using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// Who is at the reporting door: a machine, named by the token it presented
/// (ADR 0016). It is not a <see cref="Caller"/> and never becomes one — a
/// machine token is not an identity, so there is no name to write into a
/// history row and nothing to attribute a change to.
/// </summary>
public sealed record MachineCaller(Guid TokenId, Guid MachineId, string MachineKey)
{
    public static MachineCaller Of(MachineToken token, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(machine);

        return new MachineCaller(token.Id, machine.Id, machine.Key);
    }
}

/// <summary>
/// The machine behind the request, as the one act that takes a report asks for
/// it. The HTTP adapter answers it from the request it authenticated.
/// </summary>
public interface ICallerMachine
{
    /// <summary>
    /// The authenticated machine. Asking on a request that has none is a bug in
    /// the adapter: exactly one endpoint is behind this door.
    /// </summary>
    MachineCaller Machine { get; }
}
