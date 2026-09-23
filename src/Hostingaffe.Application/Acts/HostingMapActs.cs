using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Application.Acts;

public sealed record HostingMapProviderShape(string Key, string Name);

public sealed record HostingMapMachineShape(
    string Key, string Name, MachineKind Kind, Status Status, string? Provider,
    string? Ipv4, string? Ipv6, string? PrivateIp, Avatar? Avatar, AvatarColor? AvatarColor);

public sealed record HostingMapShape(
    IReadOnlyList<HostingMapProviderShape> Providers,
    IReadOnlyList<HostingMapMachineShape> Machines);

/// <summary>One read for the provider-to-machine navigation view, including inherited VM providers.</summary>
public sealed class ReadHostingMap(IProviders providers, IMachines machines)
{
    public async Task<HostingMapShape> ExecuteAsync(CancellationToken cancellationToken)
    {
        var providerRows = await providers.ListAsync(cancellationToken);
        var machineRows = await machines.ListAsync(null, null, retired: true, cancellationToken);
        var effective = await machines.ProviderKeysAsync(machineRows.Select(machine => machine.Id), cancellationToken);

        return new HostingMapShape(
            [.. providerRows.Select(provider => new HostingMapProviderShape(provider.Key, provider.Name))],
            [.. machineRows.Select(machine => new HostingMapMachineShape(
                machine.Key, machine.Name, machine.Kind, machine.Status,
                effective.GetValueOrDefault(machine.Id),
                machine.Ipv4, machine.Ipv6, machine.PrivateIp, machine.Avatar, machine.AvatarColor))]);
    }
}
