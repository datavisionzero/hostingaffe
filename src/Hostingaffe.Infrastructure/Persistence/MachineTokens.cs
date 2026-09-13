using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The machine tokens. The lookup is the one every other token has: hash the
/// presented secret, find the row by the unique index, read nothing else.
/// </summary>
public sealed class MachineTokens(HostingaffeDbContext context) : IMachineTokens
{
    public async Task<(MachineToken Token, Machine Machine)?> FindByHashAsync(
        byte[] secretHash, CancellationToken cancellationToken)
    {
        // A deleted machine's token admits nobody: the machine is invisible, and
        // a report about something nobody can see would have nowhere to be read.
        var found = await context.MachineTokens
            .Where(token => token.SecretHash == secretHash && token.RevokedAt == null)
            .Join(
                context.Machines.Where(machine => machine.DeletedAt == null),
                token => token.MachineId,
                machine => machine.Id,
                (token, machine) => new { token, machine })
            .SingleOrDefaultAsync(cancellationToken);

        return found is null ? null : (found.token, found.machine);
    }

    public Task<MachineToken?> LiveForAsync(Guid machineId, CancellationToken cancellationToken) =>
        context.MachineTokens.SingleOrDefaultAsync(
            token => token.MachineId == machineId && token.RevokedAt == null, cancellationToken);

    public Task<MachineToken?> MostRecentAsync(Guid machineId, CancellationToken cancellationToken) =>
        context.MachineTokens
            .Where(token => token.MachineId == machineId)
            .OrderByDescending(token => token.RevokedAt == null)
            .ThenByDescending(token => token.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(MachineToken token) => context.MachineTokens.Add(token);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
