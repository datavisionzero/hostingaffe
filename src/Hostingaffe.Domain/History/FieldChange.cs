namespace Hostingaffe.Domain.History;

/// <summary>
/// One field that changed, as the history records it: what, from what, to what
/// (<c>docs/storage.md</c>, The history).
/// </summary>
/// <remarks>
/// It lives with the history rather than with any one entity because every
/// entity that keeps one produces these, and the values are already spelled the
/// way the history and the API spell them — which is the point: the row is
/// written from the same list the change was made from and cannot drift from
/// it.
/// </remarks>
public sealed record FieldChange(string Field, string? OldValue, string? NewValue);
