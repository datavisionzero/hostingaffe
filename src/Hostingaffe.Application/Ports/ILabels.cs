using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Ports;

/// <summary>The label rows of a project.</summary>
public interface ILabels
{
    /// <summary>The live labels of a project, by name.</summary>
    Task<IReadOnlyList<Label>> ListAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>By name within the project, deleted or not.</summary>
    Task<Label?> FindAsync(Guid projectId, string name, CancellationToken cancellationToken);

    /// <exception cref="Domain.Refusal"><c>validation</c> on <c>name</c> when the unique index refuses it.</exception>
    Task AddAsync(Label label, CancellationToken cancellationToken);

    /// <inheritdoc cref="AddAsync"/>
    Task SaveAsync(Label label, CancellationToken cancellationToken);

    /// <summary>
    /// The keys of the live issues and epics that carry <paramref name="label"/>
    /// and another live label of <paramref name="group"/> — everything that
    /// would end up with two of one group if the label moved there. The rule is
    /// the same on both (<c>docs/storage.md</c>, Labels), so the question is too.
    /// </summary>
    Task<GroupClash> ClashesWithGroupAsync(Label label, string group, CancellationToken cancellationToken);
}

/// <summary>
/// What stands in the way of a group change, kept apart by what it is: the two
/// lists reach the caller as <c>issues</c> and <c>epics</c>, and a reader who
/// only knows <c>issues</c> still reads the issues right.
/// </summary>
public sealed record GroupClash(IReadOnlyList<string> Issues, IReadOnlyList<string> Epics)
{
    public int Count => Issues.Count + Epics.Count;

    /// <summary>The keys of both, issues first.</summary>
    public IReadOnlyList<string> Keys => [.. Issues, .. Epics];
}
