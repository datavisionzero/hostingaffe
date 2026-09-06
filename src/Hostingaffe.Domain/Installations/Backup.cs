namespace Hostingaffe.Domain.Installations;

/// <summary>
/// The backup decision (<c>CONTEXT.md</c>, Installation). Closed, and a field
/// rather than prose, because "every production installation without a backup"
/// is a question the product answers in one line (VISION 7).
/// </summary>
public enum Backup
{
    /// <summary>None, decided or not — which is the same thing to whoever loses the data.</summary>
    None,

    /// <summary>Decided on, not there yet.</summary>
    Planned,

    /// <summary>Running.</summary>
    Active,
}
