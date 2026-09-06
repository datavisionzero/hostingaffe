using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>The lookups every issue act starts with.</summary>
public static class IssueLookup
{
    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public static async Task<IssueRow> LiveAsync(
        this IIssues issues, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number))
        {
            throw new Refusal(RefusalCode.NotFound, $"{key} is not an issue key.");
        }

        var row = await issues.FindLiveAsync(projectKey, number, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        var deleted = await issues.FindDeletedAsync(projectKey, number, cancellationToken);
        return deleted is null
            ? throw new Refusal(RefusalCode.NotFound, $"No issue {IssueKey.Of(projectKey, number)}.")
            : throw new Refusal(
                RefusalCode.Deleted,
                $"Issue {deleted.Key} is deleted and can be restored until at least {deleted.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = deleted.DeletedAt.Value + settings.DeletionGrace });
    }

    /// <summary>The labels named, resolved in the project, live; groups enforced within the set.</summary>
    /// <exception cref="Refusal"><c>unknown-label</c>, or <c>validation</c> when two share a group.</exception>
    public static async Task<IReadOnlyList<Label>> ResolveLabelsAsync(
        this ILabels labels, Project project, IEnumerable<string> names, string field, CancellationToken cancellationToken)
    {
        var live = (await labels.ListAsync(project.Id, cancellationToken)).ToDictionary(l => l.Name, StringComparer.Ordinal);
        var resolved = new List<Label>();

        foreach (var name in names.Select(n => n?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal))
        {
            if (!live.TryGetValue(name, out var label))
            {
                throw new Refusal(RefusalCode.UnknownLabel, $"Project {project.Key} has no label {name}.", new Dictionary<string, object?> { ["label"] = name });
            }

            resolved.Add(label);
        }

        var clash = resolved.Where(l => l.Group is not null).GroupBy(l => l.Group).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw Refusal.Validation(field, $"{string.Join(" and ", clash.Select(l => l.Name))} are both of the group {clash.Key}; a group admits one label at a time.");
        }

        return resolved;
    }
}
