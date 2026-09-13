using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The machine tokens (<c>docs/storage.md</c>, Machine tokens): the one key a
/// machine may hold, which hands in a report for itself and reads nothing
/// (ADR 0016).
/// </summary>
public interface IMachineTokens
{
    /// <summary>
    /// The live token with this hash, and the machine it belongs to. Revoked
    /// rows never answer, and neither does a token whose machine is deleted.
    /// </summary>
    Task<(MachineToken Token, Machine Machine)?> FindByHashAsync(
        byte[] secretHash, CancellationToken cancellationToken);

    /// <summary>The live token of this machine, tracked, or nothing where it has none.</summary>
    Task<MachineToken?> LiveForAsync(Guid machineId, CancellationToken cancellationToken);

    /// <summary>
    /// The live token of this machine, or the last one it had where there is
    /// none: "is it still reporting, and is that the token's doing" is one
    /// question, and a revoked row is half of the answer.
    /// </summary>
    Task<MachineToken?> MostRecentAsync(Guid machineId, CancellationToken cancellationToken);

    void Add(MachineToken token);

    Task SaveAsync(CancellationToken cancellationToken);
}
