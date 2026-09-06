namespace Hostingaffe.Domain.Issues;

/// <summary>
/// The one fixed set an issue moves through (<c>CONTEXT.md</c>, Status). Not
/// configurable, no variants: <c>backlog</c> and <c>todo</c> answer
/// <em>when</em>, <c>in_progress</c> is a claim, <c>review</c> waits for a human,
/// and the two closed ones are a decision that stays visible.
/// </summary>
/// <remarks>
/// A status is not written by a client; it changes through the acts of ADR
/// 0016, parking excepted. An issue is born in <see cref="Todo"/> (VISION 9).
/// </remarks>
public enum IssueStatus
{
    Backlog,
    Todo,
    InProgress,
    Review,
    Done,
    Canceled,
}
