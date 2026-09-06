namespace Hostingaffe.Domain.Machines;

/// <summary>One field that changed, as the history records it: what, from what, to what.</summary>
public sealed record FieldChange(string Field, string? OldValue, string? NewValue);

/// <summary>
/// What a caller wants a machine to say. Every field is optional; <c>null</c>
/// leaves the field alone, and the empty string clears a text field
/// (<c>docs/api.md</c>, Machines).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Host"/> is the row of the host machine rather than its key: the
/// Domain does not resolve keys, so the act does that and hands the row down.
/// <see cref="HostGiven"/> is what tells "leave the host alone" from "clear
/// it", because a cleared host arrives as a null row either way.
/// </para>
/// <para>
/// <see cref="Kind"/>, <see cref="Arch"/>, <see cref="Status"/> and
/// <see cref="MeasuredAt"/> are set, never cleared. A closed set has no empty
/// value to send, and a measurement is corrected by measuring again.
/// </para>
/// </remarks>
public sealed record MachineEdit
{
    public string? Name { get; init; }

    public string? Hostname { get; init; }

    public MachineKind? Kind { get; init; }

    public Machine? Host { get; init; }

    public bool HostGiven { get; init; }

    public string? Provider { get; init; }

    public string? Plan { get; init; }

    public string? Location { get; init; }

    public string? Os { get; init; }

    public Arch? Arch { get; init; }

    public string? Cpu { get; init; }

    public string? Memory { get; init; }

    public string? Disk { get; init; }

    public string? Ipv4 { get; init; }

    public string? Ipv6 { get; init; }

    public string? PrivateIp { get; init; }

    public string? Ssh { get; init; }

    public Status? Status { get; init; }

    public DateTimeOffset? MeasuredAt { get; init; }

    public string? Description { get; init; }
}
