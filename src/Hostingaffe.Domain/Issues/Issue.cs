namespace Hostingaffe.Domain.Issues;

/// <summary>
/// The unit of work (<c>CONTEXT.md</c>, Issue): the field set of VISION 8
/// without <c>release</c>, which is cut two.
/// </summary>
/// <remarks>
/// <para>
/// Three of its invariants are also check constraints on the table, because
/// each is a rule the vision states and a state the product must never show
/// (<c>docs/storage.md</c>, Issues): it is <c>in_progress</c> exactly when
/// somebody holds a <see cref="Claim"/> on it; it is closed exactly when
/// <see cref="ClosedAt"/> is set; and the claim's columns come and go together.
/// The acts that move an issue between those states — claim, release, close,
/// review, reopen, park — are ADR 0016's and arrive with their tickets; what is
/// here is the birth state and the shape.
/// </para>
/// <para>
/// The key — <c>PLAN-42</c> — is not stored. It is the project's key and
/// <see cref="Number"/>, joined at read time.
/// </para>
/// </remarks>
public sealed class Issue
{
    public const int TitleMaxLength = 200;

    private Issue()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Issue(Guid id, Guid projectId, int number, string title, Guid authorId, DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Number = number;
        Title = title;
        Description = string.Empty;
        Status = IssueStatus.Todo;
        Priority = Priority.None;
        AuthorId = authorId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    public Guid ProjectId { get; private init; }

    /// <summary>The number in <c>PLAN-42</c>, drawn from the project's issue counter.</summary>
    public int Number { get; private init; }

    public string Title { get; private set; } = null!;

    /// <summary>The assignment: what is to be done, as Markdown.</summary>
    public string Description { get; private set; } = null!;

    /// <summary>What was actually done — or, on <c>canceled</c>, why it will not be.</summary>
    public string? Result { get; private set; }

    public IssueStatus Status { get; private set; }

    /// <summary>Concrete enough to implement without asking first (VISION 10).</summary>
    public bool Ready { get; private set; }

    public Priority Priority { get; private set; }

    /// <summary>Who it belongs to — which is not what the claim says.</summary>
    public Guid? AssigneeId { get; private set; }

    public Guid? EpicId { get; private set; }

    /// <summary>The issue this sub-issue is part of, or <c>null</c> for a top-level issue.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Who is working now, or <c>null</c>.</summary>
    public Claim? Claim { get; private set; }

    public Guid AuthorId { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    /// <summary>Derived, not a status: <c>done</c> or <c>canceled</c> (<c>CONTEXT.md</c>, Closed).</summary>
    public bool Closed => Status is IssueStatus.Done or IssueStatus.Canceled;

    public bool Deleted => DeletedAt is not null;

    /// <summary>
    /// An issue as it is born: in <c>todo</c>, not ready, priority none, with
    /// nobody assigned and nobody working (VISION 8, 9).
    /// </summary>
    public static Issue Create(Guid projectId, int number, string title, Guid authorId, DateTimeOffset createdAt, bool parked = false)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "An issue number is drawn from one upwards.");
        }

        var issue = new Issue(Guid.CreateVersion7(), projectId, number, NormalizeTitle(title), authorId, createdAt);
        if (parked)
        {
            // `status: "backlog"` in a create parks it from birth (docs/api.md);
            // the explicit act of parking is `Park`, which arrives with the moves.
            issue.Status = IssueStatus.Backlog;
        }

        return issue;
    }

    public void Retitle(string title, DateTimeOffset at)
    {
        Title = NormalizeTitle(title);
        UpdatedAt = at;
    }

    /// <summary>The assignment; <c>null</c> clears it to nothing.</summary>
    public void Describe(string? description, DateTimeOffset at)
    {
        Description = description ?? string.Empty;
        UpdatedAt = at;
    }

    /// <summary>What was done, or on <c>canceled</c> why not; <c>null</c> clears it.</summary>
    public void RecordResult(string? result, DateTimeOffset at)
    {
        Result = string.IsNullOrWhiteSpace(result) ? null : result;
        UpdatedAt = at;
    }

    public void Prioritize(Priority priority, DateTimeOffset at)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority is 0 to 4.");
        }

        Priority = priority;
        UpdatedAt = at;
    }

    /// <summary>
    /// The flag as a statement about the issue, and anybody who may change the
    /// issue may write it: <em>triage required</em> decides what <c>next</c>
    /// hands out, not who says the word (ADR 0019).
    /// </summary>
    public void SetReady(bool ready, DateTimeOffset at)
    {
        Ready = ready;
        UpdatedAt = at;
    }

    /// <summary>Who it belongs to, or nobody — which is the normal case (VISION 8).</summary>
    public void Assign(Guid? assigneeId, DateTimeOffset at)
    {
        AssigneeId = assigneeId;
        UpdatedAt = at;
    }

    /// <summary>Under an epic, or under none. Whether the epic reopens is the store's business (VISION 7).</summary>
    public void AttachTo(Guid? epicId, DateTimeOffset at)
    {
        EpicId = epicId;
        UpdatedAt = at;
    }

    /// <summary>Make this issue a sub-issue, or detach it. Cross-row hierarchy rules are checked by the act.</summary>
    public void AttachToParent(Guid? parentId, DateTimeOffset at)
    {
        if (parentId == Id)
        {
            throw new Refusal(RefusalCode.OneLevel, "An issue cannot be its own parent.");
        }

        ParentId = parentId;
        UpdatedAt = at;
    }

    /// <summary>
    /// The act the product exists for (VISION 11): claim this issue for
    /// <paramref name="holder"/>. Unclaimed or expired: taken. Held by the
    /// caller: extended. Held by somebody else: <c>claim-held</c> — unless
    /// <paramref name="force"/>, which takes it over, and never an agent over a
    /// user's claim (<c>claim-protected</c>). Sets <c>in_progress</c>; one step,
    /// not two.
    /// </summary>
    /// <remarks>
    /// On an open issue not in <c>review</c>, whatever else it is — parked,
    /// blocked, waiting on a question — because workability is <c>next</c>'s
    /// rule, not the claim's. The refusals that name the other holder carry
    /// the holder's id under <c>holder</c>; the adapter renders the name.
    /// </remarks>
    /// <exception cref="Refusal"><c>transition</c>, <c>claim-held</c> or <c>claim-protected</c>.</exception>
    public ClaimOutcome ClaimFor(
        Guid holder, Identities.IdentityKind holderKind, bool force, DateTimeOffset at, TimeSpan agentDeadline)
    {
        if (Closed)
        {
            throw new Refusal(RefusalCode.Transition, "A closed issue is not claimed; reopen it first.");
        }

        if (Status is IssueStatus.Review)
        {
            throw new Refusal(RefusalCode.Transition, "The issue has been handed over for review; send it back to todo first (VISION 11).");
        }

        var current = Claim is { } claim && !claim.ExpiredAt(at) ? claim : null;
        var expired = Claim is { } lapsed && lapsed.ExpiredAt(at) ? lapsed : null;

        if (current is not null && current.HolderId == holder)
        {
            Claim = current.Extended(holderKind, at, agentDeadline);
            UpdatedAt = at;
            return new ClaimOutcome(ClaimOutcomeKind.Extended, null);
        }

        if (current is not null)
        {
            if (!force)
            {
                throw new Refusal(
                    RefusalCode.ClaimHeld,
                    $"The issue is held since {current.ClaimedAt:u}; pass force to take it over.",
                    new Dictionary<string, object?> { ["holder"] = current.HolderId });
            }

            if (current.ExpiresAt is null && holderKind is Identities.IdentityKind.Agent)
            {
                throw new Refusal(
                    RefusalCode.ClaimProtected,
                    "A user's claim does not expire and is taken over only by a user (ADR 0015).",
                    new Dictionary<string, object?> { ["holder"] = current.HolderId });
            }
        }

        var previous = current?.HolderId ?? expired?.HolderId;
        Claim = Issues.Claim.Take(holder, holderKind, at, agentDeadline);
        Status = IssueStatus.InProgress;
        UpdatedAt = at;

        return new ClaimOutcome(
            current is not null ? ClaimOutcomeKind.Forced
            : expired is not null ? ClaimOutcomeKind.TakenAfterExpiry
            : ClaimOutcomeKind.Taken,
            previous);
    }

    /// <summary>
    /// Let go: the claim is cleared and the status is <c>todo</c>, wherever the
    /// claim started (VISION 11). The caller has been checked to be the holder
    /// or a user; here the issue only has to be held.
    /// </summary>
    /// <returns>The holder that let go.</returns>
    /// <exception cref="Refusal"><c>transition</c> when nobody holds it.</exception>
    public Guid Release(DateTimeOffset at)
    {
        if (Claim is null || Claim.ExpiredAt(at) || Status is not IssueStatus.InProgress)
        {
            throw new Refusal(RefusalCode.Transition, "Nobody holds this issue.");
        }

        var holder = Claim.HolderId;
        Claim = null;
        Status = IssueStatus.Todo;
        UpdatedAt = at;
        return holder;
    }

    /// <summary>
    /// Close (VISION 9, ADR 0014): from any open status. A user's close lands
    /// where it says; an agent's lands there too unless review is required,
    /// when it lands in <c>review</c> — <c>canceled</c> included — with the
    /// result kept for the reviewer. Out of <c>review</c>, an agent's close goes
    /// through only where review is not required. Clears the claim; sets
    /// <c>closed_at</c> on a real close.
    /// </summary>
    /// <returns>Where it landed.</returns>
    /// <exception cref="Refusal"><c>validation</c> on a target that is not a close; <c>transition</c>.</exception>
    public IssueStatus Close(IssueStatus target, string? result, Identities.IdentityKind by, bool reviewRequired, DateTimeOffset at)
    {
        if (target is not (IssueStatus.Done or IssueStatus.Canceled))
        {
            throw Refusal.Validation("status", "A close is done or canceled.");
        }

        if (Closed)
        {
            throw new Refusal(RefusalCode.Transition, "The issue is closed already; reopen it first.");
        }

        var agentUnderReview = by is Identities.IdentityKind.Agent && reviewRequired;

        if (Status is IssueStatus.Review && agentUnderReview)
        {
            throw new Refusal(RefusalCode.Transition, "The issue is in review, where an agent's close lands; a user accepts it from here (ADR 0014).");
        }

        if (result is not null)
        {
            RecordResult(result, at);
        }

        Claim = null;

        if (agentUnderReview)
        {
            Status = IssueStatus.Review;
            ClosedAt = null;
        }
        else
        {
            Status = target;
            ClosedAt = at;
        }

        UpdatedAt = at;
        return Status;
    }

    /// <summary>
    /// Hand in explicitly, whatever the switch says: from any open status but
    /// <c>review</c>. Clears the claim, no <c>closed_at</c> (VISION 9).
    /// </summary>
    /// <exception cref="Refusal"><c>transition</c>.</exception>
    public void HandIn(string? result, DateTimeOffset at)
    {
        if (Closed || Status is IssueStatus.Review)
        {
            throw new Refusal(RefusalCode.Transition, Closed ? "A closed issue is not handed in; reopen it first." : "The issue is in review already.");
        }

        if (result is not null)
        {
            RecordResult(result, at);
        }

        Claim = null;
        Status = IssueStatus.Review;
        UpdatedAt = at;
    }

    /// <summary>
    /// The one movement to <c>todo</c> from <c>review</c>, <c>done</c> or
    /// <c>canceled</c>: <c>closed_at</c> cleared, no claim, the result kept
    /// until the next close overwrites it (VISION 9).
    /// </summary>
    /// <exception cref="Refusal"><c>transition</c>.</exception>
    public void Reopen(DateTimeOffset at)
    {
        if (Status is not (IssueStatus.Review or IssueStatus.Done or IssueStatus.Canceled))
        {
            throw new Refusal(RefusalCode.Transition, "Only an issue in review, done or canceled is reopened.");
        }

        Status = IssueStatus.Todo;
        ClosedAt = null;
        Claim = null;
        UpdatedAt = at;
    }

    /// <summary>
    /// Park, or unpark: the one status move that is a field write (ADR 0016),
    /// <c>todo</c> to <c>backlog</c> and back, on an open, unclaimed issue.
    /// </summary>
    /// <exception cref="Refusal"><c>transition</c> for every other cell of the table.</exception>
    public void MoveTo(IssueStatus target, DateTimeOffset at)
    {
        var allowed = (Status, target) is (IssueStatus.Todo, IssueStatus.Backlog) or (IssueStatus.Backlog, IssueStatus.Todo);
        if (!allowed || (Claim is { } claim && !claim.ExpiredAt(at)))
        {
            throw new Refusal(
                RefusalCode.Transition,
                target is IssueStatus.Backlog or IssueStatus.Todo
                    ? $"Parking moves todo to backlog and back, on an open, unclaimed issue; this one is {Status.ToString().ToLowerInvariant()}."
                    : "The status is changed through the acts — claim, release, close, review, reopen — not through PATCH.");
        }

        Claim = null;
        Status = target;
        UpdatedAt = at;
    }

    /// <summary>
    /// The soft delete of ADR 0013: invisible everywhere, restorable for the
    /// grace period. Deleting lets go of the claim, and where the issue was
    /// <c>in_progress</c> the status becomes <c>todo</c>, because the invariant
    /// allows nothing else and the claim does not come back on restore.
    /// </summary>
    public void Delete(Guid by, DateTimeOffset at)
    {
        if (Deleted)
        {
            return;
        }

        if (Claim is not null || Status is IssueStatus.InProgress)
        {
            Claim = null;
            Status = IssueStatus.Todo;
        }

        DeletedAt = at;
        DeletedBy = by;
        UpdatedAt = at;
    }

    /// <summary>Back into whatever state it was in, without its claim (ADR 0013).</summary>
    public void Restore(DateTimeOffset at)
    {
        DeletedAt = null;
        DeletedBy = null;
        UpdatedAt = at;
    }

    /// <summary>
    /// A write by the holder extends the claim; a write by anybody else leaves
    /// it alone (VISION 11) — a human asking "how far did you get?" must not
    /// keep a dead claim alive.
    /// </summary>
    public void ExtendClaimIfHeldBy(Guid writer, Identities.IdentityKind writerKind, DateTimeOffset at, TimeSpan agentDeadline)
    {
        if (Claim is { } claim && !claim.ExpiredAt(at) && claim.HolderId == writer)
        {
            Claim = claim.Extended(writerKind, at, agentDeadline);
        }
    }

    /// <summary>
    /// A change to the issue's attachments — a label, a blocker, a comment —
    /// moves its version, so that a guarded write sees it (<c>docs/api.md</c>,
    /// Concurrency on text fields).
    /// </summary>
    public void Touch(DateTimeOffset at) => UpdatedAt = at;

    /// <exception cref="ArgumentException">
    /// <paramref name="title"/> is blank, spans lines, or is longer than
    /// <see cref="TitleMaxLength"/>.
    /// </exception>
    public static string NormalizeTitle(string title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("An issue has a title.", nameof(title));
        }

        return trimmed.Length > TitleMaxLength || trimmed.Contains('\n')
            ? throw new ArgumentException(
                $"An issue title is one line of at most {TitleMaxLength} characters.", nameof(title))
            : trimmed;
    }
}
