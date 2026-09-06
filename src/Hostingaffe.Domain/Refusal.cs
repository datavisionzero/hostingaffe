namespace Hostingaffe.Domain;

/// <summary>
/// The product saying no: a <see cref="RefusalCode"/>, a sentence for a person,
/// and whatever the code needs beside it — the object on <c>stale</c>, the
/// offending fields on <c>validation</c>.
/// </summary>
/// <remarks>
/// One type rather than one per code, because every adapter turns all of them
/// into the same document (<c>docs/api.md</c>, Errors) and the CLI into an exit
/// code, and neither wants a catch clause per rule. Extension members are keyed
/// the way the wire spells them, in <c>snake_case</c>, so that nothing between
/// here and the document has to rename them.
/// </remarks>
public sealed class Refusal(
    RefusalCode code,
    string detail,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(detail)
{
    public RefusalCode Code { get; } = code;

    public string Detail { get; } = detail;

    public IReadOnlyDictionary<string, object?> Extensions { get; } =
        extensions ?? new Dictionary<string, object?>();

    /// <summary>
    /// The <c>validation</c> refusal: which fields, and what is wrong with each.
    /// </summary>
    public static Refusal Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(
            RefusalCode.Validation,
            errors.Count == 1
                ? $"{errors.First().Key}: {string.Join("; ", errors.First().Value)}"
                : $"{errors.Count} fields are missing, malformed or over their limit.",
            new Dictionary<string, object?> { ["errors"] = errors });

    /// <inheritdoc cref="Validation(IReadOnlyDictionary{string, string[]})"/>
    public static Refusal Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });
}
