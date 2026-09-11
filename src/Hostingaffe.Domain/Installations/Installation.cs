using Hostingaffe.Domain.History;

namespace Hostingaffe.Domain.Installations;

/// <summary>
/// One software installed once on one machine (<c>CONTEXT.md</c>, Installation;
/// VISION 7). Two logaffe installations on the same host are two installations;
/// Caddy on three hosts is three installations of one software.
/// </summary>
/// <remarks>
/// <para>
/// The word stands where "service" would: systemd and Compose both mean
/// something else by it, and a runbook saying "restart the service" would be
/// ambiguous in a product that also knows a systemd unit as a file.
/// </para>
/// <para>
/// <see cref="MachineId"/> and <see cref="SoftwareId"/> are two of the three
/// relationships the model builds; <see cref="DependsOn"/> is the third, which
/// VISION 15.2 held back until the first real host showed that the other two do
/// not carry "what falls out if I touch this" (ADR 0014).
/// </para>
/// <para>
/// <strong>There is no version field.</strong> The current version is the one of
/// the latest deployment by <c>at</c>, computed on read; a column repeating it
/// would be exactly the second truth VISION 7 avoids.
/// </para>
/// </remarks>
public sealed class Installation
{
    public const int NameMaxLength = 200;

    /// <summary>How many entries one list holds — enough for anything real, and a bound on the row.</summary>
    public const int ListMaxCount = 50;

    /// <summary>
    /// The ports, in the field EF Core fills: the navigation itself is read
    /// only, because a port is added by replacing the list and never by reaching
    /// into it.
    /// </summary>
    private readonly List<Port> _ports = [];

    /// <summary>
    /// The secrets, in the field EF Core fills — read only for the reason the
    /// ports are: a list is replaced whole and never reached into.
    /// </summary>
    private readonly List<Secret> _secrets = [];

    /// <summary>
    /// What it needs, in the field EF Core fills — read only for the reason the
    /// ports are: a list is replaced whole and never reached into.
    /// </summary>
    private readonly List<Dependency> _dependsOn = [];

    private Installation()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Installation(
        Guid id,
        string key,
        string name,
        Guid machineId,
        Guid softwareId,
        Environment environment,
        Role role,
        Guid createdBy,
        DateTimeOffset createdAt)
    {
        Id = id;
        Key = key;
        Name = name;
        MachineId = machineId;
        SoftwareId = softwareId;
        Environment = environment;
        Role = role;
        Status = Status.Active;
        Backup = Backup.None;
        Monitoring = Monitoring.None;
        Logging = Logging.Local;
        Urls = [];
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

    /// <summary>The machine it is installed on. Required, and never cleared.</summary>
    public Guid MachineId { get; private set; }

    /// <summary>What it is an installation of. Required, and never cleared.</summary>
    public Guid SoftwareId { get; private set; }

    /// <summary>Whom it serves.</summary>
    public Environment Environment { get; private set; }

    /// <summary>What it is for the machine.</summary>
    public Role Role { get; private set; }

    public Status Status { get; private set; }

    /// <summary>Where it is reachable, if anywhere.</summary>
    public string[] Urls { get; private set; } = [];

    /// <summary>
    /// What it listens on, and how far each one reaches. A row of its own per
    /// port, so that <c>protocol</c> and <c>scope</c> are columns with the check
    /// constraint every other closed set has (<c>docs/storage.md</c>).
    /// </summary>
    public IReadOnlyList<Port> Ports => _ports;

    /// <summary>
    /// Where it lives on the machine — <c>/srv/logaffe</c>: the directory it is
    /// deployed from, and the one every file it owns lies under.
    /// </summary>
    public string? Path { get; private set; }

    /// <summary>
    /// Where its persistent data lies — <c>/srv/services/logaffe</c>: what a
    /// backup has to take and what a <c>docker compose down -v</c> does not
    /// bring back. The second of an installation's two directories, and on a
    /// host that keeps configuration and state together it is the first
    /// (ADR 0009).
    /// </summary>
    public string? Data { get; private set; }

    /// <summary>
    /// The secrets it needs: each one named, and each one saying which file on
    /// the machine its value lies in. Never a value — those live in vaultaffe or
    /// on the host (ADR 0011).
    /// </summary>
    public IReadOnlyList<Secret> Secrets => _secrets;

    /// <summary>
    /// The installations this one needs to do its job: the reverse proxy in
    /// front of it, the database beside it. One hop and never a closure — a
    /// transitive list would put this installation under one it never named,
    /// which is the second truth VISION 7 avoids (ADR 0014).
    /// </summary>
    /// <remarks>
    /// The entries are ids; the keys are the act's to resolve. What needs
    /// <em>this</em> installation is the same rows read the other way and is
    /// derived on read, never stored.
    /// </remarks>
    public IReadOnlyList<Dependency> DependsOn => _dependsOn;

    /// <summary>The backup decision, as a field so that it can be listed.</summary>
    public Backup Backup { get; private set; }

    public Monitoring Monitoring { get; private set; }

    public Logging Logging { get; private set; }

    /// <summary>Markdown: the runbook — how it is deployed, checked, updated, rolled back, and what its data is.</summary>
    public string Description { get; private set; } = null!;

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>The version a guarded write is compared against.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Deleted => DeletedAt is not null;

    /// <exception cref="ArgumentException">The key, the name or one of the two words does not hold.</exception>
    public static Installation Create(
        string key,
        string? name,
        Guid machineId,
        Guid softwareId,
        Environment environment,
        Role role,
        Guid createdBy,
        DateTimeOffset createdAt)
    {
        var handle = Domain.Key.Normalize(key);

        return new Installation(
            Guid.CreateVersion7(),
            handle,
            string.IsNullOrWhiteSpace(name) ? handle : NormalizeName(name),
            machineId,
            softwareId,
            Enum.IsDefined(environment) ? environment : throw new ArgumentException("Not an environment.", nameof(environment)),
            Enum.IsDefined(role) ? role : throw new ArgumentException("Not a role.", nameof(role)),
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
    public IReadOnlyList<FieldChange> Apply(InstallationEdit edit, Guid by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var changes = new List<FieldChange>();

        Fields.Text("name", edit.Name, Name, value => Name = value ?? Key, NormalizeName, changes);
        Fields.Text("path", edit.Path, Path, value => Path = value, NormalizePath, changes);
        Fields.Text("data", edit.Data, Data, value => Data = value, NormalizeData, changes);

        Fields.Closed("environment", edit.Environment, Environment, value => Environment = value, changes);
        Fields.Closed("role", edit.Role, Role, value => Role = value, changes);
        Fields.Closed("status", edit.Status, Status, value => Status = value, changes);
        Fields.Closed("backup", edit.Backup, Backup, value => Backup = value, changes);
        Fields.Closed("monitoring", edit.Monitoring, Monitoring, value => Monitoring = value, changes);
        Fields.Closed("logging", edit.Logging, Logging, value => Logging = value, changes);

        Fields.Many(
            "urls",
            Fields.InField("urls", () => edit.Urls?.Select(url => Fields.Url(url?.Trim() ?? string.Empty, "url")).ToArray()),
            Urls,
            value => Urls = [.. value],
            url => url,
            ListMaxCount,
            changes);

        // A secret is the same secret by its name, whatever it says about where
        // it lies: an installation needs `POSTGRES_PASSWORD` once, and two rows
        // naming two files would be a contradiction rather than two secrets.
        Fields.Many(
            "secrets",
            edit.Secrets,
            Secrets,
            value =>
            {
                _secrets.Clear();
                _secrets.AddRange(value);
            },
            secret => secret.ToString(),
            ListMaxCount,
            changes,
            secret => secret.Name);

        // Two ports on the same number and protocol are one port, whatever
        // their scopes say, so that is what makes them the same entry.
        Fields.Many(
            "ports",
            edit.Ports,
            Ports,
            value =>
            {
                _ports.Clear();
                _ports.AddRange(value);
            },
            port => port.ToString(),
            ListMaxCount,
            changes,
            port => $"{port.Number}/{Spelling.Of(port.Protocol)}");

        // A machine and a software are named by key on the wire, and that is
        // what the history says: an id would be true and unreadable.
        if (edit.Machine is { } machine && machine.Id != MachineId)
        {
            changes.Add(new FieldChange("machine", null, machine.Key));
            MachineId = machine.Id;
        }

        if (edit.Software is { } software && software.Id != SoftwareId)
        {
            changes.Add(new FieldChange("software", null, software.Key));
            SoftwareId = software.Id;
        }

        // The third relationship, and rows rather than keys for the reason the
        // two above are: the Domain resolves nothing. The list has no order of
        // its own, so it is held in key order — which makes a caller who sends
        // the same set in another order no change at all, and an export of it
        // the same bytes twice.
        if (edit.DependsOn is { } dependencies)
        {
            var wanted = InOrder(dependencies);

            if (!_dependsOn.Select(one => one.DependsOnId).ToHashSet().SetEquals(wanted.Select(one => one.Id)))
            {
                _dependsOn.Clear();
                _dependsOn.AddRange(wanted.Select(one => Dependency.On(one.Id)));

                // What the list became, and not what it was. The rows hold ids,
                // and the keys a person reads are the act's to resolve; the
                // previous history row carries the previous list, which is what
                // makes the pair readable without the Domain looking anything up
                // (docs/storage.md, A list records what it became).
                changes.Add(new FieldChange(
                    "depends_on", null, Fields.Joined(wanted, one => one.Key)));
            }
        }

        // A description records that it changed, never how: the history is read
        // for what happened, and the text itself is one read away.
        if (edit.Description is not null && edit.Description != Description)
        {
            changes.Add(new FieldChange("description", null, null));
            Description = edit.Description;
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

    /// <summary>
    /// The dependencies a caller gave, checked and put in key order. An
    /// installation does not depend on itself: that is not a relationship, it is
    /// a typo, and the one shape of cycle worth refusing. A longer one is not
    /// refused — the product reads one hop and computes no closure, so a cycle
    /// costs nothing to hold, and two services that need each other exist
    /// (ADR 0014).
    /// </summary>
    /// <exception cref="ArgumentException">There are too many, one is this one, or one is named twice.</exception>
    private IReadOnlyList<Installation> InOrder(IReadOnlyList<Installation> given)
    {
        if (given.Count > ListMaxCount)
        {
            throw new ArgumentException($"A depends_on list holds at most {ListMaxCount} entries.", "depends_on");
        }

        var ordered = given.OrderBy(one => one.Key, StringComparer.Ordinal).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var one in ordered)
        {
            if (one.Id == Id)
            {
                throw new ArgumentException($"{Key} would depend on itself.", "depends_on");
            }

            if (!seen.Add(one.Key))
            {
                throw new ArgumentException($"{one.Key} is in depends_on twice.", "depends_on");
            }
        }

        return ordered;
    }

    /// <exception cref="ArgumentException"><paramref name="name"/> is blank, spans lines, or is too long.</exception>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? throw new ArgumentException("An installation has a name.", nameof(name))
            : Fields.Line(trimmed, NameMaxLength, "An installation name");
    }

    /// <summary>
    /// Where the installation lives on the machine: an absolute path, because
    /// that is what it is on the machine and what <c>files sync</c> will write
    /// under.
    /// </summary>
    /// <exception cref="ArgumentException">It is not an absolute path, or it climbs.</exception>
    public static string NormalizePath(string path) =>
        Fields.Absolute(path, "A path is where the installation lives on the machine, from the root: /srv/logaffe.");

    /// <summary>
    /// Where the installation's persistent data lies: the same shape as
    /// <see cref="NormalizePath"/>, and a second directory because an
    /// installation has two of them (ADR 0009). Nothing holds the two apart —
    /// one directory may be the other's parent, or the same directory, and on a
    /// host that keeps configuration and state together it is.
    /// </summary>
    /// <exception cref="ArgumentException">It is not an absolute path, or it climbs.</exception>
    public static string NormalizeData(string path) =>
        Fields.Absolute(path, "Data is where the installation's persistent data lies, from the root: /srv/services/logaffe.");
}
