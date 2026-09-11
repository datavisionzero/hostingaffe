using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// What a deletion takes with it and what a restore brings back
/// (<c>CONTEXT.md</c>, Retired and deleted; planaffe ADR 0013).
/// </summary>
/// <remarks>
/// <para>
/// A deleted machine takes its installations, its files and their deployments;
/// a deleted installation takes its files and its deployments. A page does
/// not follow its anchor — it survives, still naming what it hung on, and only
/// the purge unhooks it.
/// </para>
/// <para>
/// <strong>What a restore brings back is what the deletion took</strong>, and
/// nothing else. Every row a cascade touches is stamped with the parent's
/// <c>deleted_at</c>, to the microsecond, and a restore brings back exactly the
/// rows carrying that stamp. A file somebody deleted on its own the week before
/// carries a different one and stays deleted, which is the honest answer: the
/// restore undoes one act, not every act.
/// </para>
/// </remarks>
public sealed class Cascade(
    IInstallations installations,
    IFiles files,
    IDeployments deployments,
    IMachines machines,
    IHistory history)
{
    /// <summary>Everything under a machine, in one stroke, stamped with its moment.</summary>
    public async Task DeleteUnderMachineAsync(
        Machine machine, Guid by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        await FilesOfAsync(AnchorKind.Machine, [machine.Id], machine.Key, by, at, cancellationToken);

        foreach (var installation in await installations.OnMachineAsync(machine.Id, null, cancellationToken))
        {
            await DeleteUnderInstallationAsync(installation, by, at, cancellationToken);

            installation.Delete(by, at);
            history.Add(HistoryEntry.OnInstallation(
                installation.Id, by, at, HistoryField.Deleted, null, null, $"with machine {machine.Key}"));
        }

        // A vm on it goes with it: the model has no machine running on one that
        // is not there.
        foreach (var guest in await machines.OnHostAsync(machine.Id, null, cancellationToken))
        {
            await DeleteUnderMachineAsync(guest, by, at, cancellationToken);

            guest.Delete(by, at);
            history.Add(HistoryEntry.OnMachine(
                guest.Id, by, at, HistoryField.Deleted, null, null, $"with machine {machine.Key}"));
        }
    }

    /// <summary>Everything under an installation: its files and its deployments.</summary>
    public async Task DeleteUnderInstallationAsync(
        Installation installation, Guid by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);

        await FilesOfAsync(AnchorKind.Installation, [installation.Id], installation.Key, by, at, cancellationToken);

        foreach (var deployment in await deployments.UnderAsync([installation.Id], null, cancellationToken))
        {
            deployment.Delete(by, at);
            history.Add(HistoryEntry.OnDeployment(
                deployment.Id, by, at, HistoryField.Deleted, null, null, $"with installation {installation.Key}"));
        }
    }

    /// <summary>What the deletion of this machine took, brought back with it.</summary>
    public async Task RestoreUnderMachineAsync(
        Machine machine, Guid by, DateTimeOffset took, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        await RestoreFilesAsync(AnchorKind.Machine, [machine.Id], by, took, at, cancellationToken);

        foreach (var installation in await installations.OnMachineAsync(machine.Id, took, cancellationToken))
        {
            await RestoreUnderInstallationAsync(installation, by, took, at, cancellationToken);

            installation.Restore();
            history.Add(HistoryEntry.OnInstallation(
                installation.Id, by, at, HistoryField.Restored, null, null, $"with machine {machine.Key}"));
        }

        foreach (var guest in await machines.OnHostAsync(machine.Id, took, cancellationToken))
        {
            await RestoreUnderMachineAsync(guest, by, took, at, cancellationToken);

            guest.Restore();
            history.Add(HistoryEntry.OnMachine(
                guest.Id, by, at, HistoryField.Restored, null, null, $"with machine {machine.Key}"));
        }
    }

    /// <summary>What the deletion of this installation took, brought back with it.</summary>
    public async Task RestoreUnderInstallationAsync(
        Installation installation, Guid by, DateTimeOffset took, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);

        await RestoreFilesAsync(AnchorKind.Installation, [installation.Id], by, took, at, cancellationToken);

        foreach (var deployment in await deployments.UnderAsync([installation.Id], took, cancellationToken))
        {
            deployment.Restore();
            history.Add(HistoryEntry.OnDeployment(
                deployment.Id, by, at, HistoryField.Restored, null, null, $"with installation {installation.Key}"));
        }
    }

    private async Task FilesOfAsync(
        AnchorKind kind, Guid[] ids, string key, Guid by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        foreach (var file in await files.UnderAsync(kind, ids, null, cancellationToken))
        {
            file.Delete(by, at);
            history.Add(HistoryEntry.OnFile(
                file.Id, by, at, HistoryField.Deleted, null, null, $"with {Spelling.Of(kind)} {key}"));
        }
    }

    private async Task RestoreFilesAsync(
        AnchorKind kind, Guid[] ids, Guid by, DateTimeOffset took, DateTimeOffset at, CancellationToken cancellationToken)
    {
        foreach (var file in await files.UnderAsync(kind, ids, took, cancellationToken))
        {
            file.Restore();
            history.Add(HistoryEntry.OnFile(file.Id, by, at, HistoryField.Restored));
        }
    }
}

/// <summary>Delete and restore a machine, with everything it carries (ADR 0013).</summary>
/// <remarks>
/// It carries its installations, and an installation on another machine may
/// depend on one of them. Deleting the machine would take that installation out
/// from under a dependent the deletion never named, so it is refused for the
/// reason deleting the installation on its own is — and the number it says is
/// the dependents that are not going with it (ADR 0014).
/// </remarks>
public sealed class MoveMachine(
    ICallerIdentity callerIdentity,
    IMachines machines,
    IInstallations installations,
    Cascade cascade,
    IHistory history,
    ITransactions transactions,
    MachineAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task DeleteAsync(string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await machines.LiveAsync(key, settings, cancellationToken);

        // Before anything else, and said with a number, for the reason a
        // software carrying installations says one.
        await NothingElsewhereDependsAsync(before.Key, before.Id, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var row = await machines.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No machine {key}.");

            // Asked again inside the transaction: the first answer was read
            // outside it, and a dependent may have arrived since.
            await NothingElsewhereDependsAsync(row.Key, row.Id, cancellationToken);

            var now = clock.GetUtcNow();

            await cascade.DeleteUnderMachineAsync(row, caller.Id, now, cancellationToken);

            row.Delete(caller.Id, now);
            history.Add(HistoryEntry.OnMachine(row.Id, caller.Id, now, HistoryField.Deleted, note: said));

            await machines.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<MachineShape> RestoreAsync(string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await machines.AnyAsync(key, cancellationToken);

        if (!before.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"Machine {before.Key} is not deleted.");
        }

        var machine = await transactions.RunAsync(async () =>
        {
            var row = await machines.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No machine {key}.");

            var took = row.DeletedAt!.Value;
            var now = clock.GetUtcNow();

            await cascade.RestoreUnderMachineAsync(row, caller.Id, took, now, cancellationToken);

            row.Restore();
            history.Add(HistoryEntry.OnMachine(row.Id, caller.Id, now, HistoryField.Restored, note: said));

            await machines.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(machine, cancellationToken);
    }

    /// <summary>
    /// Whether anything that stays behind depends on something that would go.
    /// A dependent on this same machine is going with it and is no reason to
    /// refuse: what the deletion takes, it takes whole.
    /// </summary>
    /// <exception cref="Refusal"><c>transition</c>, with <c>dependents</c> saying how many.</exception>
    private async Task NothingElsewhereDependsAsync(string key, Guid id, CancellationToken cancellationToken)
    {
        var here = (await installations.OnMachineAsync(id, null, cancellationToken))
            .Select(one => one.Id)
            .ToHashSet();

        var hanging = (await installations.DependentsAsync(here, cancellationToken))
            .SelectMany(pair => pair.Value)
            .Where(dependent => !here.Contains(dependent))
            .Distinct()
            .Count();

        if (hanging > 0)
        {
            throw new Refusal(
                RefusalCode.Transition,
                $"{key} carries installations that {hanging} installation(s) on other machines depend on; a machine is not deleted out from under them. Clear their depends_on, or retire it instead.",
                new Dictionary<string, object?> { ["dependents"] = hanging });
        }
    }
}

/// <summary>
/// Delete and restore a software — and the one refusal in this whole ticket:
/// a software still carrying installations is not deleted, quietly or
/// otherwise.
/// </summary>
public sealed class MoveSoftware(
    ICallerIdentity callerIdentity,
    ISoftware software,
    IInstallations installations,
    IHistory history,
    ITransactions transactions,
    SoftwareAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task DeleteAsync(string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await software.LiveAsync(key, settings, cancellationToken);

        // Before anything else, and said with a number, because "delete it and
        // see" is exactly what a cascade here would have been.
        var hanging = await installations.CountOnSoftwareAsync(before.Id, cancellationToken);
        if (hanging > 0)
        {
            throw new Refusal(
                RefusalCode.Transition,
                $"{before.Key} still has {hanging} installation(s); a software is not deleted out from under them.",
                new Dictionary<string, object?> { ["installations"] = hanging });
        }

        await transactions.RunAsync(async () =>
        {
            var row = await software.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No software {key}.");

            // Asked again inside the transaction: the first answer was read
            // outside it, and an installation may have arrived since.
            var still = await installations.CountOnSoftwareAsync(row.Id, cancellationToken);
            if (still > 0)
            {
                throw new Refusal(
                    RefusalCode.Transition,
                    $"{row.Key} still has {still} installation(s); a software is not deleted out from under them.",
                    new Dictionary<string, object?> { ["installations"] = still });
            }

            var now = clock.GetUtcNow();
            row.Delete(caller.Id, now);
            history.Add(HistoryEntry.OnSoftware(row.Id, caller.Id, now, HistoryField.Deleted, note: said));

            await software.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<SoftwareShape> RestoreAsync(string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await software.AnyAsync(key, cancellationToken);

        if (!before.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"Software {before.Key} is not deleted.");
        }

        var row = await transactions.RunAsync(async () =>
        {
            var found = await software.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No software {key}.");

            var now = clock.GetUtcNow();
            found.Restore();
            history.Add(HistoryEntry.OnSoftware(found.Id, caller.Id, now, HistoryField.Restored, note: said));

            await software.SaveAsync(cancellationToken);
            return found;
        }, cancellationToken);

        return await assembler.CompleteAsync(row, cancellationToken);
    }
}

/// <summary>Delete and restore an installation, with its files and its deployments.</summary>
/// <remarks>
/// An installation others depend on is not deleted out from under them, the way
/// a software carrying installations is not: the answer is a number and not a
/// cascade, because an edge is not a possession and sweeping it away would take
/// a fact out of somebody else's record. Retiring is the normal end and keeps
/// every edge (<c>CONTEXT.md</c>, Retired and deleted; ADR 0014).
/// </remarks>
public sealed class MoveInstallation(
    ICallerIdentity callerIdentity,
    IInstallations installations,
    Cascade cascade,
    IHistory history,
    ITransactions transactions,
    InstallationAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task DeleteAsync(string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await installations.LiveAsync(key, settings, cancellationToken);

        // Before anything else, and said with a number, for the reason a
        // software carrying installations says one.
        await NothingDependsAsync(before.Key, before.Id, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var row = await installations.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No installation {key}.");

            // Asked again inside the transaction: the first answer was read
            // outside it, and a dependent may have arrived since.
            await NothingDependsAsync(row.Key, row.Id, cancellationToken);

            var now = clock.GetUtcNow();

            await cascade.DeleteUnderInstallationAsync(row, caller.Id, now, cancellationToken);

            row.Delete(caller.Id, now);
            history.Add(HistoryEntry.OnInstallation(row.Id, caller.Id, now, HistoryField.Deleted, note: said));

            await installations.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<InstallationShape> RestoreAsync(string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await installations.AnyAsync(key, cancellationToken);

        if (!before.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"Installation {before.Key} is not deleted.");
        }

        var installation = await transactions.RunAsync(async () =>
        {
            var row = await installations.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No installation {key}.");

            var took = row.DeletedAt!.Value;
            var now = clock.GetUtcNow();

            await cascade.RestoreUnderInstallationAsync(row, caller.Id, took, now, cancellationToken);

            row.Restore();
            history.Add(HistoryEntry.OnInstallation(row.Id, caller.Id, now, HistoryField.Restored, note: said));

            await installations.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(installation, cancellationToken);
    }

    /// <exception cref="Refusal"><c>transition</c>, with <c>dependents</c> saying how many.</exception>
    private async Task NothingDependsAsync(string key, Guid id, CancellationToken cancellationToken)
    {
        var hanging = await installations.CountDependentsAsync(id, cancellationToken);

        if (hanging > 0)
        {
            throw new Refusal(
                RefusalCode.Transition,
                $"{key} still has {hanging} installation(s) depending on it; an installation is not deleted out from under them. Clear their depends_on, or retire this one instead.",
                new Dictionary<string, object?> { ["dependents"] = hanging });
        }
    }
}

/// <summary>
/// Delete and restore a file. Its revisions go with it and come back with it:
/// they are what the file is, not something beside it.
/// </summary>
public sealed class MoveFile(
    ICallerIdentity callerIdentity,
    IFiles files,
    FileLookup lookup,
    IHistory history,
    ITransactions transactions,
    FileAssembler assembler,
    TimeProvider clock)
{
    public async Task DeleteAsync(
        AnchorKind kind, string key, string path, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var owner = await lookup.OwnerAsync(kind, key, cancellationToken);
        var before = await lookup.LiveAsync(owner, path, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var row = await files.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No file {path} of {owner}.");

            var now = clock.GetUtcNow();
            row.Delete(caller.Id, now);
            history.Add(HistoryEntry.OnFile(row.Id, caller.Id, now, HistoryField.Deleted, note: said));

            await files.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<FileShape> RestoreAsync(
        AnchorKind kind, string key, string path, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var owner = await lookup.OwnerAsync(kind, key, cancellationToken);
        var before = await lookup.AnyAsync(owner, path, cancellationToken);

        if (!before.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"The file {before.Path} of {owner} is not deleted.");
        }

        var file = await transactions.RunAsync(async () =>
        {
            var row = await files.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No file {path} of {owner}.");

            var now = clock.GetUtcNow();
            row.Restore();
            history.Add(HistoryEntry.OnFile(row.Id, caller.Id, now, HistoryField.Restored, note: said));

            await files.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(owner, file, file.Current, cancellationToken);
    }
}

/// <summary>
/// Delete and restore a deployment. This is the other half of the correction
/// rule: a deployment with the wrong version is deleted and recorded again.
/// </summary>
public sealed class MoveDeployment(
    ICallerIdentity callerIdentity,
    IDeployments deployments,
    DeploymentLookup lookup,
    IHistory history,
    ITransactions transactions,
    DeploymentAssembler assembler,
    TimeProvider clock)
{
    public async Task DeleteAsync(string key, int number, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var installation = await lookup.InstallationAsync(key, cancellationToken);
        var before = await lookup.LiveAsync(installation, number, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var row = await deployments.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"{key} has no deployment {number}.");

            var now = clock.GetUtcNow();
            row.Delete(caller.Id, now);
            history.Add(HistoryEntry.OnDeployment(row.Id, caller.Id, now, HistoryField.Deleted));

            await deployments.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<DeploymentShape> RestoreAsync(string key, int number, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var installation = await lookup.InstallationAsync(key, cancellationToken);

        var before = await deployments.FindAnyAsync(installation.Id, number, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"{key} has no deployment {number}.");

        if (!before.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"Deployment {number} of {key} is not deleted.");
        }

        var deployment = await transactions.RunAsync(async () =>
        {
            var row = await deployments.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"{key} has no deployment {number}.");

            var now = clock.GetUtcNow();
            row.Restore();
            history.Add(HistoryEntry.OnDeployment(row.Id, caller.Id, now, HistoryField.Restored));

            await deployments.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(
            installation, await lookup.AllAsync(installation, cancellationToken), deployment, cancellationToken);
    }
}
