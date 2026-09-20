using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.Installations;

namespace Hostingaffe.Application.Acts;

public sealed record InstallationMapEntryShape(
    string Key, string Name, string Software, Role Role, Status Status,
    IReadOnlyList<string> Urls, DateTimeOffset? LatestDeploymentAt);

public sealed record InstallationMapShape(
    string Machine, string Name, Status Status, IReadOnlyList<InstallationMapEntryShape> Installations);

/// <summary>One read of a machine and its recorded installation map facts.</summary>
public sealed class ReadInstallationMap(
    IMachines machines, IInstallations installations, IDeployments deployments,
    ISoftware software, InstanceSettings settings)
{
    public async Task<InstallationMapShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var machine = await machines.LiveAsync(key, settings, cancellationToken);
        var rows = await installations.ListAsync(
            new InstallationFilter { MachineId = machine.Id, Retired = true }, cancellationToken);
        var softwareKeys = await software.KeysAsync(rows.Select(row => row.SoftwareId), cancellationToken);
        var deploymentRows = await deployments.ListAsync(rows.Select(row => row.Id), cancellationToken);
        var latest = deploymentRows.GroupBy(row => row.InstallationId)
            .ToDictionary(group => group.Key, group => Derived.Latest(group)!.At);

        var entries = rows.Select(row => new InstallationMapEntryShape(
                row.Key, row.Name, softwareKeys[row.SoftwareId], row.Role, row.Status,
                row.Urls, latest.TryGetValue(row.Id, out var at) ? at : null))
            .OrderBy(entry => entry.LatestDeploymentAt is null)
            .ThenByDescending(entry => entry.LatestDeploymentAt)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();

        return new InstallationMapShape(machine.Key, machine.Name, machine.Status, entries);
    }
}
