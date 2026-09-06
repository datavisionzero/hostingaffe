using System.Text.RegularExpressions;
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
/// <see cref="MachineId"/> is the second and last relationship the model builds.
/// <c>depends_on</c> is roadmap (VISION 15.2) and is not a column here, not even
/// a nullable one prepared in advance.
/// </para>
/// <para>
/// <strong>There is no version field.</strong> The current version is the one of
/// the latest deployment by <c>at</c>, computed on read; a column repeating it
/// would be exactly the second truth VISION 7 avoids.
/// </para>
/// </remarks>
public sealed partial class Installation
{
    public const int NameMaxLength = 200;

    /// <summary>What a path on a machine fits in.</summary>
    public const int PathMaxLength = 500;

    /// <summary>What the name of a secret fits in.</summary>
    public const int SecretNameMaxLength = 200;

    /// <summary>How many entries one list holds — enough for anything real, and a bound on the row.</summary>
    public const int ListMaxCount = 50;

    /// <summary>
    /// The shape of a secret's <em>name</em>: a word, not a sentence and not a
    /// value. Whitespace and <c>=</c> fall outside it, which is what keeps the
    /// line of an <c>.env</c> file from being pasted in here whole.
    /// </summary>
    public const string SecretNamePattern = "^[A-Za-z_][A-Za-z0-9_./-]*$";

    /// <summary>
    /// The ports, in the field EF Core fills: the navigation itself is read
    /// only, because a port is added by replacing the list and never by reaching
    /// into it.
    /// </summary>
    private readonly List<Port> _ports = [];

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
        Secrets = [];
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

    /// <summary>Where it lives on the machine — <c>/srv/logaffe</c>.</summary>
    public string? Path { get; private set; }

    /// <summary>The <em>names</em> of the secrets it needs. Never the values; those live in vaultaffe or on the host.</summary>
    public string[] Secrets { get; private set; } = [];

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

        Fields.Many(
            "secrets",
            Fields.InField("secrets", () => edit.Secrets?.Select(NormalizeSecretName).ToArray()),
            Secrets,
            value => Secrets = [.. value],
            name => name,
            ListMaxCount,
            changes);

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
    public static string NormalizePath(string path)
    {
        var trimmed = path?.Trim() ?? string.Empty;
        Fields.Line(trimmed, PathMaxLength, "A path");

        if (!trimmed.StartsWith('/'))
        {
            throw new ArgumentException("A path is where the installation lives on the machine, from the root: /srv/logaffe.");
        }

        if (trimmed.Split('/').Any(segment => segment is ".."))
        {
            throw new ArgumentException("A path does not climb; .. is not part of one.");
        }

        // One trailing slash is what a person types and means nothing; the root
        // is the one path that is a slash.
        return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }

    /// <summary>
    /// The name of a secret, and only ever a name: the values live in vaultaffe
    /// or on the host, never here (VISION 7).
    /// </summary>
    /// <exception cref="ArgumentException">It is not the shape of a name.</exception>
    public static string NormalizeSecretName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        Fields.Line(trimmed, SecretNameMaxLength, "A secret name");

        return SecretName().IsMatch(trimmed)
            ? trimmed
            : throw new ArgumentException(
                $"A secret is named, never given: a name is one word ({SecretNamePattern}), and the value belongs nowhere near here.",
                "secrets");
    }

    [GeneratedRegex(SecretNamePattern)]
    private static partial Regex SecretName();
}
