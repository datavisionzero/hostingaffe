# A File Has One Owner, and What Else It Concerns Is Prose

A file is owned by exactly one machine or one installation — the one whose
directory it lies in — and carries nothing that names a second one it concerns.
A shared reverse proxy's site fragment is the proxy installation's file, and
the installation it fronts says so **in its description**: the path, and an
ordinary link to the proxy. The check constraint stays
`num_nonnulls(machine_id, installation_id) = 1`.

## What forced it

A host with one proxy in front of everything is the ordinary way to run one,
and on such a host the model reads oddly. `/srv/caddy/sites/hostingaffe.caddy`
is Caddy's file by every rule there is: it lies under Caddy's `path`, Caddy
reloads it, and `ha files sync --installation caddy` writes it. It is also the
first thing somebody reads who wants to know how hostingaffe is reached, it
changes when hostingaffe changes, and `ha inst view hostingaffe` does not show
it. On the first host with a shared Caddy that is seven fragments for seven
services, each filed where the service's own reader does not look.

## The alternatives

**A second owner.** The wrong repair, and the constraint is right to refuse it.
A file lies in one directory and is written by one sync; two owners would make
`ha files sync --installation hostingaffe` a claim on a directory that belongs
to Caddy, and the first sync would either write into it or quietly leave one of
the owners' files out.

**An optional field on the file naming the installation it concerns.** This is
the one that nearly won, and three things stand against it.

It is [Vision §15.2](../../Vision.md#152-an-installation-depends-on-an-installation)
entered through the side door. The relationship being described is between two
*installations* — hostingaffe is reached through Caddy — and the fragment is a
consequence of that, not the thing itself. When the deferred relation arrives it
will explain the fragment along with the port, the network alias and what breaks
when the proxy is down, and there would then be two ways to say the same thing,
one of them narrower.

The cardinality is not one. A fragment can front two installations, Caddy's own
TLS snippet concerns all seven, and a machine's `daemon.json` concerns every
installation on the box. A single optional reference is wrong for half the cases
it would be reached for, and the moment it becomes a list it is a relation
table — an edge type in a model that
[Vision §7](../../Vision.md#7-domain-model) closed at two.

It buys a filter nobody has asked for. "Which files concern this installation"
is a question that arises while reading the installation, where a sentence
answers it, and not as a query across the record.

**Why the directory was a field and this is not.**
[ADR 0008](./0008-a-machines-file-says-where-it-lies-and-is-never-synced.md)
rejected prose for a machine file's directory in as many words — "a record whose
most important fact is prose is the repository this product replaces" — and the
difference is real. Without its directory a systemd unit is a text nobody can
put back: the record holds the content and not the one fact that makes it
usable. The fragment is complete without a reference. It is at the right path,
under the right owner, and sync writes it correctly. What a reference would add
is that a second reader finds it from the other side — a reading order, not the
file's own truth. The first is a column; the second is a sentence.

## What carries it instead

The description of an installation is its runbook (Vision §7), and where the
service is reached from is part of operating it. This is not new ground: the
[`hostaffe`](https://github.com/datavisionzero/hostaffe) template this product
replaces already answers it there, with a line under **Network** in every
service's own document: `Caddy site fragment:
/srv/caddy/sites/<service>.caddy`. Here that line is better than it was,
because the proxy is a link the interface resolves
([ADR 0007](./0007-a-record-is-linked-from-markdown-as-a-scheme-and-a-key.md)):

```md
Reached at https://hostingaffe.example.com. The TLS endpoint is
`sites/hostingaffe.caddy` in [caddy](installation:caddy).
```

The path stays a code span beside the link rather than becoming one, because a
file is addressed by an owner and a path and has no scheme of its own (ADR
0007). And the fragment is
findable from the record itself: its content names the service it fronts, so
`ha search hostingaffe` returns it and says the match was in the content.

## Consequences

- **Nothing in the schema changes.** `file` keeps its owner check, `ha files
  list --installation KEY` keeps meaning "the files this installation owns", and
  `ha machine context` keeps listing each file once, under its owner.
- **`CONTEXT.md` says how a shared proxy is meant**, under File, so that nobody
  migrating a host derives it again from scratch.
- **The AGENTS.md block says it too** ([`docs/agents-md.md`](../agents-md.md)),
  because a convention is worth what the agents following it know of it.
- **The description is unchecked, and that is the cost.** Nothing refuses an
  installation whose fragment is nowhere named, and nothing notices when the
  path in the sentence goes stale. `ha page check` reports dead links in pages,
  not in descriptions.
- **Vision §15.2 is where this is answered if it stops being enough**, as a
  relation between installations rather than a field on the file. It stopped
  being enough on the first host with a shared proxy, and the relation is
  [ADR 0014](./0014-an-installation-depends-on-an-installation-and-the-reverse-is-derived.md).
  It carries the relationship and not the fragment's path: that is still the
  sentence in the description, for the reason this decision gives.
