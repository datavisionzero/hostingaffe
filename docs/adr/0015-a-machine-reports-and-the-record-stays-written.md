# A Machine Reports, and the Record Stays Written

A machine may hand in a **report**: what it knows about itself at a moment —
disk usage, memory, load, and what Docker runs. The instance keeps it beside
the record and **never writes any of it into the record**. No field of a
machine or an installation is set from a report, no history row is made, and
there is no button that pulls one side after the other. Where the two disagree,
that is drift, and which of them is right is a person's or an agent's decision.

## What forced it

[Vision §15.1](../../Vision.md#151-measuring-instead-of-typing) asked for
measuring instead of typing, and ended on the sentence that turned out to
decide the shape: "the same output compared against the record is the cheapest
possible drift check — the machine says Docker 29.7.2, the record says 29.1.2,
measured 40 days ago."

That comparison needs two sides. The obvious design — the machine measures and
writes its findings into the record — has only one afterwards, and nobody can
then say what the documentation claimed and what the machine actually does. The
drift check disappears into the thing it was supposed to check.

[Vision §5](../../Vision.md#5-non-goals-deliberate-boundaries) says the record
is written, not observed. A machine that writes itself is observation with the
extra step of the record forgetting it ever disagreed.

## The decision

**A report is a sample beside the record.** It belongs to exactly one machine,
has no key — the instance numbers it per machine, as it does a deployment — and
is never edited. It is written once, read, and eventually swept.

**A report writes nothing.** Not `os`, not `arch`, not a version, not
`measured_at`. The one thing it does move is `last_used_at` of the token that
delivered it, which is about the token and not about the machine.

**A report is not a history entry.** The history is who changed the record. A
cron reporting every quarter of an hour has changed nothing, and ninety-six
rows a day would bury every real change in a machine's history. What *is* a
history entry is a person issuing or revoking a machine's token, because that
is a change to the record.

**`last_seen` is derived**, the `received_at` of the latest report, never a
column that could fall out of step with the reports it summarises — the same
reason `needed_by` is derived and never written
([ADR 0014](./0014-an-installation-depends-on-an-installation-and-the-reverse-is-derived.md)).

**The drift is computed on read and stored nowhere.** The container image tag
against the version of the installation's latest deployment, `os`, `arch` and
the Docker version against the machine's fields with their `measured_at`
beside them. Where the assignment is ambiguous — two installations of the same
software on one machine — the report is shown and nothing is claimed. A wrong
sentence is worse than no sentence, because the reader would have to check it
against the two rows anyway.

**The sections are closed.** `host`, `memory`, `disks`, `containers`, and the
list of what could not be determined — no free-form field beside them. A body
that takes anything becomes a metric database in six months, and then a
threshold appears, and then an alert
([Vision §5](../../Vision.md#5-non-goals-deliberate-boundaries)).

## The alternatives

**The machine writes its measured facts into the record.** This is `ha machine
facts | ha machine set --facts -` from §15.1, and it is the design that loses
the comparison. It has a second cost: a field somebody wrote by hand, with a
note about why the number is what it is, is overwritten at the next quarter of
an hour by a machine that does not know about the note. Rejected.

**A "reconcile" button that copies one report field into the record.** The same
thing, made deliberate. It sounds harmless and is discovery through the back
door: the record would then be a cache of the machine, and the question "what
did we intend here" no longer has a place to live. What a person can always do
is read the drift and type the new value, which is one edit with a note and an
author — and that is the record doing its job.

**A report as a history row.** It puts the sample where somebody will look for
it and destroys what they came for. Rejected on volume as much as on meaning.

**A time series of the numbers.** The disk percentage of every report, kept
forever, graphed. That is a monitoring product, and three of them are named in
§5 as the ones that do it. What is kept instead is thirty days of samples, with
the latest report of a machine exempt so that a machine that fell silent still
says when it last spoke.

## Consequences

- **Two tables and no columns elsewhere**: `machine_report` and
  `machine_token`. The machine gains nothing, because `last_seen` is derived
  (`docs/storage.md`).
- **A report is delivered by a machine token and by nothing else.** A user or
  agent token is refused at that endpoint: someone who could post a report by
  hand could forge the drift comparison without it showing anywhere
  ([ADR 0016](./0016-a-machine-token-posts-one-report-and-reads-nothing.md)).
- **`collected_at` is kept, `received_at` decides.** The host's clock is
  recorded as it came and is trusted for nothing; the order of reports and
  `last_seen` are read from the instance's own clock. A `collected_at` in the
  future is stored rather than refused — refusing it would deny a machine with
  a wrong clock its sign of life.
- **Reports go with the machine.** A soft delete takes them, a restore brings
  them back, a purge takes them for good — unlike the history, which survives
  everything. A sample has nothing to tell once the machine is gone.
- **The machine's `monitoring` sibling field does not change.** An
  installation's `monitoring` keeps `none · planned · external`; there is no
  `internal`, because a report is not a monitor.

## Since

The set of sections named above is the one this decision was taken with. Two
have been added since, each by a decision of its own and each still closed:

- **`listening`** — per port and protocol, the port and how far the socket is
  bound. It carries no process, and that is the whole of what was decided: a
  process name needs root, and the collector's promise is that it needs none.
  It gave the comparison a fourth kind, `port`.
- **`updates`** — whether the machine is waiting for a restart, and nothing
  more. The count of pending packages was deliberately left out: it would make
  the collector distribution-dependent, and a host whose package lists are
  weeks old reports nothing pending and lies in the most comforting way there
  is.

What that changes about this decision: nothing. A report still sets no field,
writes no history row, carries no secret, and is still read by no machine
token. A section proposed that weakened any of those four would be refused
rather than planned.
