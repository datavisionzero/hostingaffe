using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>A label as the API shows it. The suffix is dropped in the contract.</summary>
public sealed record LabelShape(string Name, string? Group, string? Description)
{
    public static LabelShape Of(Label label) => new(label.Name, label.Group, label.Description);
}

/// <summary>What a <c>PATCH</c> carries; a field set to <c>null</c> clears it, an absent one leaves it.</summary>
/// <param name="Group">Present with <c>null</c> takes the label out of its group.</param>
public sealed record LabelChanges(string? Name, bool GroupGiven, string? Group, bool DescriptionGiven, string? Description);

/// <summary>Every live label of a project with its group and description — the project's schema, for an agent.</summary>
public sealed class ListLabels(IProjects projects, ProjectScope scope, ILabels labels, InstanceSettings settings)
{
    public async Task<IReadOnlyList<LabelShape>> ExecuteAsync(string projectKey, CancellationToken cancellationToken)
    {
        var project = await projects.LiveAsync(projectKey, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);

        return [.. (await labels.ListAsync(project.Id, cancellationToken)).Select(LabelShape.Of)];
    }
}

/// <summary>Anyone creates a label; labels are the one extensibility the product offers, and agents use it.</summary>
public sealed class CreateLabel(IProjects projects, ProjectScope scope, ILabels labels, InstanceSettings settings, TimeProvider clock)
{
    public async Task<LabelShape> ExecuteAsync(
        string projectKey, string? name, string? group, string? description, CancellationToken cancellationToken)
    {
        var project = await projects.LiveAsync(projectKey, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);

        var label = Validated.Field("name", () => Label.Create(project.Id, name!, null, null, clock.GetUtcNow()));
        Validated.Field("group", () => { label.Regroup(group); return true; });
        Validated.Field("description", () => { label.Describe(description); return true; });

        var existing = await labels.FindAsync(project.Id, label.Name, cancellationToken);
        if (existing is not null)
        {
            throw Refusal.Validation("name", existing.Deleted
                ? $"The label {label.Name} is deleted and can be restored; a new one cannot take its name until it is purged."
                : $"The label {label.Name} exists in {project.Key}.");
        }

        await labels.AddAsync(label, cancellationToken);

        return LabelShape.Of(label);
    }
}

/// <summary>
/// Rename, regroup or describe a label. Moving it into a group an issue or an
/// epic already carries another label of is refused with <c>validation</c> and
/// the keys under <c>issues</c> and <c>epics</c>: the alternative is one of them
/// with two of one group, which the group exists to make impossible.
/// </summary>
public sealed class ChangeLabel(IProjects projects, ProjectScope scope, ILabels labels, InstanceSettings settings)
{
    public async Task<LabelShape> ExecuteAsync(
        string projectKey, string name, LabelChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var project = await projects.LiveAsync(projectKey, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        var label = await LabelLookup.LiveAsync(labels, project, name, cancellationToken);

        if (changes.GroupGiven && changes.Group is not null && changes.Group != label.Group)
        {
            var group = Validated.Field("group", () => LabelName.Normalize(changes.Group, "group"));
            var clash = await labels.ClashesWithGroupAsync(label, group, cancellationToken);
            if (clash.Count > 0)
            {
                var carriers = Carriers(clash);
                throw new Refusal(
                    RefusalCode.Validation,
                    $"{carriers} would carry two labels of the group {group}: {string.Join(", ", clash.Keys)}.",
                    new Dictionary<string, object?>
                    {
                        ["errors"] = new Dictionary<string, string[]> { ["group"] = [$"{carriers} would carry two labels of this group."] },
                        ["issues"] = clash.Issues,
                        ["epics"] = clash.Epics,
                    });
            }
        }

        if (changes.Name is not null && changes.Name != label.Name)
        {
            var newName = Validated.Field("name", () => LabelName.Normalize(changes.Name));
            if (await labels.FindAsync(project.Id, newName, cancellationToken) is not null)
            {
                throw Refusal.Validation("name", $"The label {newName} exists in {project.Key}.");
            }

            label.Rename(newName);
        }

        if (changes.GroupGiven)
        {
            Validated.Field("group", () => { label.Regroup(changes.Group); return true; });
        }

        if (changes.DescriptionGiven)
        {
            Validated.Field("description", () => { label.Describe(changes.Description); return true; });
        }

        await labels.SaveAsync(label, cancellationToken);

        return LabelShape.Of(label);
    }

    // Only what is actually in the way is named, so that a refusal about epics
    // alone does not open by counting issues.
    private static string Carriers(GroupClash clash)
    {
        var counted = new List<string>(2);
        if (clash.Issues.Count > 0)
        {
            counted.Add($"{clash.Issues.Count} issue(s)");
        }

        if (clash.Epics.Count > 0)
        {
            counted.Add($"{clash.Epics.Count} epic(s)");
        }

        return string.Join(" and ", counted);
    }
}

/// <summary>Soft delete: the label vanishes from every issue; its attachments wait for a restore.</summary>
public sealed class DeleteLabel(IProjects projects, ProjectScope scope, ILabels labels, InstanceSettings settings, TimeProvider clock)
{
    public async Task ExecuteAsync(string projectKey, string name, CancellationToken cancellationToken)
    {
        var project = await projects.LiveAsync(projectKey, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        var label = await LabelLookup.LiveAsync(labels, project, name, cancellationToken);

        label.Delete(clock.GetUtcNow());
        await labels.SaveAsync(label, cancellationToken);
    }
}

/// <summary>Back, with its attachments. A label that is not deleted is <c>transition</c>.</summary>
public sealed class RestoreLabel(IProjects projects, ProjectScope scope, ILabels labels, InstanceSettings settings)
{
    public async Task<LabelShape> ExecuteAsync(string projectKey, string name, CancellationToken cancellationToken)
    {
        var project = await projects.LiveAsync(projectKey, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);

        var label = await labels.FindAsync(project.Id, name, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No label {name} in {project.Key}.");

        if (!label.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"The label {name} is not deleted.");
        }

        label.Restore();
        await labels.SaveAsync(label, cancellationToken);

        return LabelShape.Of(label);
    }
}

internal static class LabelLookup
{
    /// <exception cref="Refusal"><c>not-found</c>; a deleted label is not found either — restore is the one act that sees it.</exception>
    public static async Task<Label> LiveAsync(ILabels labels, Project project, string name, CancellationToken cancellationToken)
    {
        var label = await labels.FindAsync(project.Id, name, cancellationToken);

        return label is null || label.Deleted
            ? throw new Refusal(RefusalCode.NotFound, $"No label {name} in {project.Key}.")
            : label;
    }
}
