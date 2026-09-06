using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Hostingaffe.Domain.Machines;

/// <summary>
/// A computer the operator pays for or owns and can log into: a rented VPS, a
/// dedicated server, a virtual machine on one of those, or a box in the office
/// (<c>CONTEXT.md</c>, Machine; VISION 7). It exists whether or not anything is
/// installed on it.
/// </summary>
/// <remarks>
/// <para>
/// The hardware facts are text and not numbers, <see cref="Arch"/> excepted.
/// Machines are compared by eye rather than summed, and
/// <c>2×512G NVMe ZFS mirror</c> is a truer description of a disk than a
/// number would be.
/// </para>
/// <para>
/// <see cref="MeasuredAt"/> is the answer to the stale document of VISION 2: a
/// machine nobody has looked at for a year says so itself.
/// </para>
/// <para>
/// <see cref="HostId"/> is one of the two relationships the model has, and it
/// is only a <see cref="MachineKind.Vm"/>'s. Whether the host exists, and
/// whether a chain of hosts closes on itself, needs the other rows and is the
/// act's; what is here is that no other kind carries one.
/// </para>
/// </remarks>
public sealed class Machine
{
    public const int NameMaxLength = 200;

    /// <summary>What every free-text fact fits in: a line, not a paragraph.</summary>
    public const int FactMaxLength = 200;

    private Machine()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Machine(Guid id, string key, string name, MachineKind kind, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        Key = key;
        Name = name;
        Kind = kind;
        Status = Status.Active;
        Description = string.Empty;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = createdBy;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>The handle, chosen once and never changed (<c>CONTEXT.md</c>, Key).</summary>
    public string Key { get; private init; } = null!;

    /// <summary>The display name; the key where the caller gave none.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>What <c>hostnamectl</c> reports — the machine's own name, not the handle.</summary>
    public string? Hostname { get; private set; }

    public MachineKind Kind { get; private set; }

    /// <summary>Only on a <see cref="MachineKind.Vm"/>: the machine it runs on.</summary>
    public Guid? HostId { get; private set; }

    public string? Provider { get; private set; }

    public string? Plan { get; private set; }

    public string? Location { get; private set; }

    public string? Os { get; private set; }

    public Arch? Arch { get; private set; }

    public string? Cpu { get; private set; }

    public string? Memory { get; private set; }

    public string? Disk { get; private set; }

    public string? Ipv4 { get; private set; }

    public string? Ipv6 { get; private set; }

    /// <summary>On the operator's private network; either family.</summary>
    public string? PrivateIp { get; private set; }

    /// <summary>The SSH target or alias the operator uses.</summary>
    public string? Ssh { get; private set; }

    public Status Status { get; private set; }

    /// <summary>When the facts above were last verified against the machine.</summary>
    public DateTimeOffset? MeasuredAt { get; private set; }

    /// <summary>Markdown: what the machine is for, and what fits nowhere else.</summary>
    public string Description { get; private set; } = null!;

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>The version a guarded write is compared against.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Deleted => DeletedAt is not null;

    /// <exception cref="ArgumentException">The key or the name does not hold.</exception>
    public static Machine Create(string key, string? name, MachineKind kind, Guid createdBy, DateTimeOffset createdAt)
    {
        var handle = Domain.Key.Normalize(key);

        return new Machine(
            Guid.CreateVersion7(),
            handle,
            string.IsNullOrWhiteSpace(name) ? handle : NormalizeName(name),
            Named(kind),
            createdBy,
            createdAt);
    }

    /// <summary>
    /// Applies what the caller gave and answers with what actually changed, in
    /// the words the API spells the fields with — so that the history is
    /// written from the same list the change was made from and cannot drift
    /// from it.
    /// </summary>
    /// <exception cref="ArgumentException">A value does not hold; the message names the field.</exception>
    public IReadOnlyList<FieldChange> Apply(MachineEdit edit, Guid by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var changes = new List<FieldChange>();

        Text("name", edit.Name, Name, value => Name = value ?? Key, NormalizeName, changes);
        Text("hostname", edit.Hostname, Hostname, value => Hostname = value, Fact, changes);
        Text("provider", edit.Provider, Provider, value => Provider = value, Fact, changes);
        Text("plan", edit.Plan, Plan, value => Plan = value, Fact, changes);
        Text("location", edit.Location, Location, value => Location = value, Fact, changes);
        Text("os", edit.Os, Os, value => Os = value, Fact, changes);
        Text("cpu", edit.Cpu, Cpu, value => Cpu = value, Fact, changes);
        Text("memory", edit.Memory, Memory, value => Memory = value, Fact, changes);
        Text("disk", edit.Disk, Disk, value => Disk = value, Fact, changes);
        Text("ssh", edit.Ssh, Ssh, value => Ssh = value, Fact, changes);

        Text("ipv4", edit.Ipv4, Ipv4, value => Ipv4 = value, value => Address(value, AddressFamily.InterNetwork, "ipv4"), changes);
        Text("ipv6", edit.Ipv6, Ipv6, value => Ipv6 = value, value => Address(value, AddressFamily.InterNetworkV6, "ipv6"), changes);
        Text("private_ip", edit.PrivateIp, PrivateIp, value => PrivateIp = value, value => Address(value, null, "private_ip"), changes);

        if (edit.Kind is { } kind && kind != Kind)
        {
            changes.Add(new FieldChange("kind", Spelling.Of(Kind), Spelling.Of(kind)));
            Kind = Named(kind);
        }

        if (edit.Arch is { } arch && arch != Arch)
        {
            changes.Add(new FieldChange("arch", Arch is null ? null : Spelling.Of(Arch.Value), Spelling.Of(arch)));
            Arch = arch;
        }

        if (edit.Status is { } status && status != Status)
        {
            changes.Add(new FieldChange("status", Spelling.Of(Status), Spelling.Of(status)));
            Status = status;
        }

        if (edit.MeasuredAt is { } measured && measured != MeasuredAt)
        {
            changes.Add(new FieldChange("measured_at", Stamp(MeasuredAt), Stamp(measured)));
            MeasuredAt = measured;
        }

        // The host is the one field where "leave it" and "clear it" both arrive
        // as no row, so the caller says which it meant.
        if (edit.HostGiven && edit.Host?.Id != HostId)
        {
            changes.Add(new FieldChange("host", null, edit.Host?.Key));
            HostId = edit.Host?.Id;
        }

        // A description records that it changed, never how: the history is read
        // for what happened, and the text itself is one read away.
        if (edit.Description is not null && edit.Description != Description)
        {
            changes.Add(new FieldChange("description", null, null));
            Description = edit.Description;
        }

        // Only a VM has a host, and clearing the kind clears it — otherwise a
        // machine that stopped being a VM would keep a relationship the model
        // does not allow it.
        if (Kind is not MachineKind.Vm && HostId is not null)
        {
            changes.Add(new FieldChange("host", null, null));
            HostId = null;
        }

        if (changes.Count > 0)
        {
            UpdatedBy = by;
            UpdatedAt = at;
        }

        return changes;
    }

    /// <summary>Soft, with the grace period of everything else (ADR 0013).</summary>
    public void Delete(Guid by, DateTimeOffset at)
    {
        if (Deleted)
        {
            return;
        }

        DeletedAt = at;
        DeletedBy = by;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }

    /// <exception cref="ArgumentException"><paramref name="name"/> is blank, spans lines, or is too long.</exception>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A machine has a name.", nameof(name));
        }

        return trimmed.Length > NameMaxLength || trimmed.Contains('\n')
            ? throw new ArgumentException(
                $"A machine name is one line of at most {NameMaxLength} characters.", nameof(name))
            : trimmed;
    }

    private static MachineKind Named(MachineKind kind) =>
        Enum.IsDefined(kind) ? kind : throw new ArgumentException("Not a machine kind.", nameof(kind));

    /// <summary>
    /// One free-text fact: a line, trimmed, at most <see cref="FactMaxLength"/>.
    /// Hardware is described here rather than measured, so the cap is what keeps
    /// a paragraph out, not what keeps a number in.
    /// </summary>
    private static string Fact(string value) =>
        value.Length > FactMaxLength || value.Contains('\n')
            ? throw new ArgumentException($"A machine fact is one line of at most {FactMaxLength} characters.")
            : value;

    private static string Address(string value, AddressFamily? family, string field) =>
        IPAddress.TryParse(value, out var parsed) && (family is null || parsed.AddressFamily == family)
            ? parsed.ToString()
            : throw new ArgumentException(
                family switch
                {
                    AddressFamily.InterNetwork => "An ipv4 is an IPv4 address.",
                    AddressFamily.InterNetworkV6 => "An ipv6 is an IPv6 address.",
                    _ => $"A {field} is an IP address.",
                });

    private static string? Stamp(DateTimeOffset? at) =>
        at?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// The one shape every text field shares: absent leaves it, the empty
    /// string clears it, anything else is normalized and set
    /// (<c>docs/api.md</c>, Machines).
    /// </summary>
    private static void Text(
        string field,
        string? given,
        string? current,
        Action<string?> set,
        Func<string, string> normalize,
        List<FieldChange> changes)
    {
        if (given is null)
        {
            return;
        }

        var trimmed = given.Trim();
        string? value;

        try
        {
            value = trimmed.Length == 0 ? null : normalize(trimmed);
        }
        catch (ArgumentException refusal)
        {
            throw new ArgumentException(refusal.Message, field);
        }

        if (value == current)
        {
            return;
        }

        set(value);
        changes.Add(new FieldChange(field, current, value));
    }
}
