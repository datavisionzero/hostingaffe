using Microsoft.EntityFrameworkCore;
using Npgsql;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The label rows of a project.</summary>
public sealed class Labels(HostingaffeDbContext context) : ILabels
{
    public async Task<IReadOnlyList<Label>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        await context.Labels
            .Where(l => l.ProjectId == projectId && l.DeletedAt == null)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

    public Task<Label?> FindAsync(Guid projectId, string name, CancellationToken cancellationToken) =>
        context.Labels.SingleOrDefaultAsync(l => l.ProjectId == projectId && l.Name == name, cancellationToken);

    public async Task AddAsync(Label label, CancellationToken cancellationToken)
    {
        context.Labels.Add(label);
        await SaveOrRefuseTheNameAsync(label.Name, cancellationToken);
    }

    public Task SaveAsync(Label label, CancellationToken cancellationToken) =>
        SaveOrRefuseTheNameAsync(label.Name, cancellationToken);

    // The live issues and epics carrying this label and another live label of
    // the group: what a group change would leave with two of one group. Epics
    // carry labels under the same rule, so they answer the same question.
    public async Task<GroupClash> ClashesWithGroupAsync(
        Label label, string groupName, CancellationToken cancellationToken)
    {
        var issues = await (
            from issue in context.Issues
            where issue.DeletedAt == null
            where context.IssueLabels.Any(il => il.IssueId == issue.Id && il.LabelId == label.Id)
            where (
                from il in context.IssueLabels
                join other in context.Labels on il.LabelId equals other.Id
                where il.IssueId == issue.Id && other.Id != label.Id && other.DeletedAt == null && other.Group == groupName
                select il).Any()
            join project in context.Projects on issue.ProjectId equals project.Id
            orderby issue.Number
            select project.Key + "-" + issue.Number).ToListAsync(cancellationToken);

        var epics = await (
            from epic in context.Epics
            where epic.DeletedAt == null
            where context.EpicLabels.Any(el => el.EpicId == epic.Id && el.LabelId == label.Id)
            where (
                from el in context.EpicLabels
                join other in context.Labels on el.LabelId equals other.Id
                where el.EpicId == epic.Id && other.Id != label.Id && other.DeletedAt == null && other.Group == groupName
                select el).Any()
            join project in context.Projects on epic.ProjectId equals project.Id
            orderby epic.Number
            select project.Key + "-E" + epic.Number).ToListAsync(cancellationToken);

        return new GroupClash(issues, epics);
    }

    private async Task SaveOrRefuseTheNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException collision) when (collision.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "label_name",
        })
        {
            throw Refusal.Validation("name", $"The label {name} exists in this project.");
        }
    }
}
