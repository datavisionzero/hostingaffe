# hostingaffe — Product Vision

> Working title. A self-hosted, opinionated record of the machines a developer
> rents or owns, what is installed on them, the configuration each installation
> runs, and how all of that changed over time — written mostly by AI agents
> through a CLI, read and corrected by humans in a small web application.

**License:** MIT · **Status:** released — `v0.1.0`: the record through the
API, the web application and `ha`, self-hostable from one Compose file. Of
section 16, what is still outstanding is the one that decides whether this
works — our own machines moved out of their Markdown repositories into an
instance. The prior-art
research behind sections 2 and 7 is in
[`docs/research/product-category-and-prior-art.md`](docs/research/product-category-and-prior-art.md).
Section 17 lists what is still open.

## 1. Elevator Pitch

hostingaffe is the place where a solo developer or a small team keeps the truth
about their infrastructure: which machines exist, what is installed on each,
which version has been running since when, which files it runs with, and how
it is operated. It is a lightweight configuration record with Markdown and
configuration files attached, not a discovery engine and not a deployment
engine.

The record is written by whoever does the work — and today that is mostly an AI
agent on an SSH session. So the CLI comes first: an agent documents a new host,
records an update, puts a Compose file in place, or reads everything it needs
to know about a machine in one call, without ever opening a browser. The web
interface is where a human reads, searches, and corrects.

It is opinionated on purpose: one fixed set of entities, one fixed set of fields,
no custom fields, no type designer. Whoever uses it adopts our way of documenting
and configuring infrastructure — the way we already do it by hand in Markdown
repositories — and gets in return a product that a human and an agent both
understand in five minutes.

## 2. Problem

We already document our machines. Each host has a Git repository derived from
the [`hostaffe`](https://github.com/datavisionzero/hostaffe) template: a
measured current state, one Markdown file per installed service with a fixed
table of fields, runbooks, decision records, and — in the private derived
repositories — the Compose files, Caddy site fragments and systemd units that a
script copies to the host over SSH. It works, and it has taught us what a good
description of an installation contains. It also shows where Markdown and files
in a repository stop being enough:

- **One repository per machine means no view across machines.** "Which hosts
  run Caddy, and which versions?" or "what listens on port 18502 anywhere?" is a
  question nobody can answer without opening six repositories.
- **The current state goes stale silently.** A `current-state.md` is right on
  the day it was measured. Nothing tells the reader that the Docker version in
  it is three upgrades old, and no field says when a value was last verified.
- **History is a `git log`.** When an installation was updated, from which image
  to which, and why, is spread over commit messages, if it was written down at
  all. The question an operator asks most — "what changed here, and when?" —
  has no answer that can be listed.
- **An agent reads too much and writes too loosely.** To work on a host, an
  agent has to read the whole repository into its context, and when it writes
  the result back, nothing checks that the fields are all there and spelled the
  same way as on the other five machines. The structure exists only as a
  convention in a template.
- **Documentation and configuration drift apart.** The Compose file lives in
  `stacks/`, its description in `docs/services/`, and the version that actually
  runs in neither. Three places, one truth, no link between them.
- **Public product repositories cannot hold host details.** Our products are
  open source, and a worked example of *our* host in a public repository is
  reconnaissance. Host documentation therefore lives in private repositories
  next to the products, and the two drift apart.

The tools that exist for this either come from the data-centre and network world
(NetBox: racks, cables, VLANs, IP address management) or from IT service
management (GLPI, iTop, i-doit: helpdesk, contracts, procurement, ITIL), or they
are developer portals for organisations with hundreds of services and a
platform team (Backstage, Port, Cortex). All of them model far more than a
person with six machines needs, all of them expect a schema to be configured
before the first record, and none of them is built for an agent to write into
from a terminal. Section 7 says which of their ideas we borrow anyway.

### 2.1 Why not keep the Markdown repositories?

Because they are the right idea in the wrong container. Everything in
`hostaffe` that has proven itself — the fixed fields of an installation, the
measured host state, the explicit backup, monitoring and logging decisions, the
runbooks with commands, the stack directory with the files a host runs —
survives here as structure. What changes is that the fields are fields, the
history is a record, the files are versioned next to the installation they
belong to, the view spans every machine, and the writer is checked.
**hostingaffe replaces the `hostaffe` template and its derived repositories
completely**; the export (6.1) hands the Markdown tree and the files back at any
time, so nothing is locked in.

## 3. Target Group

**Primarily:** solo developers and small teams (1–5 people) who rent a few VPS
and dedicated servers, perhaps run a box or two at home, operate their
applications with Docker Compose, and have AI agents do most of the hands-on
work over SSH.

**Characteristics:** at home on the console; already using `git`, `gh`, `ssh`
and a coding agent every day; running planaffe, logaffe or vaultaffe, or
something like them.

**Not the target group (in the MVP):** data centres with racks and cabling,
managed service providers documenting *other people's* infrastructure,
organisations that need discovery agents, procurement, contracts, or a
permission matrix.

## 4. Guiding Principles

1. **Agent-first.** The agent is the primary author. Every function the web
   interface has is reachable from the CLI, and the CLI is measured by what one
   act costs in round trips and context, not by the server's latency.
2. **Opinionated instead of configurable.** One set of entities, one set of
   fields per entity, one way to record a deployment, one way to lay out the
   files of an installation. No custom fields, no type designer, no
   relationship editor. Whoever needs a field we do not have tells us why; the
   answer is a change to the product or a line in the Markdown, never a
   settings screen.
3. **Structure only where it works.** Whatever the system has to evaluate —
   filter, count, compare across machines, derive a current version from — is a
   field. Whatever is only read stays Markdown. Whatever a machine executes is
   a file.
4. **A record, not a robot.** hostingaffe stores what someone told it and hands
   files back on request. It does not scan machines, does not run `docker
   compose up`, and does not reconcile anything. The agent that did the work
   writes down what it did; the product makes that cheap and checks the shape.
5. **The history is the product.** Every deployment is a record of its own,
   every field change is recorded with who, when, from and to, and every file
   keeps its previous revisions. A value without a date and an author is a
   rumour.
6. **Text only.** Fields, Markdown, and UTF-8 text files. No binaries, no
   images, no uploads. A configuration that is not text does not belong here.
7. **Easy to host.** Postgres plus the app, two containers, one Compose file.
   No object storage, no queue, no search index beyond Postgres.
8. **Safe on the open internet.** What this product holds is exactly what an
   attacker wants first: addresses, ports, versions, paths, configuration.
   Every surface is authenticated, nothing is readable anonymously, and
   network-level protection is never the answer to a security question.
9. **MIT licence.** Fully open source, no open-core split.

## 5. Non-Goals (Deliberate Boundaries)

- **No discovery.** No agent on the machine, no SSH scanning, no SNMP, no
  container-registry polling. The record is written, not observed (a small
  assisted measurement is a roadmap item, 15.1 — it still writes only what it
  is told to).
- **No deployment engine.** hostingaffe writes an installation's files onto
  the machine when asked and records that a deployment happened. It does not
  run Compose, restart anything, or watch whether it worked. It does not
  become Ansible.
- **No monitoring, no metrics, no alerts.** Uptime Kuma, Beszel and logaffe do
  that. An installation *names* where its logs and monitors are; it does not
  receive them.
- **No secret values.** An installation lists the *names* of the secrets it
  needs and the file each one lies in on the machine. The values are
  vaultaffe's, or wherever the operator keeps them. hostingaffe refuses the
  files and paths that the template already declares secret-bearing.
- **No IPAM, DCIM, racks, cables, VLANs, subnets.** A machine has addresses;
  addresses are not an entity.
- **No custom fields, no custom entity types, no configurable relationships.**
- **No multi-tenancy, no customer separation.** One instance holds one team's
  infrastructure, and everyone in the team sees all of it.
- **No cost accounting, contracts, warranties, licences** in the MVP.
- **No binary files, no images, no attachments.** Text only.
- **No DNS management.** The template's Cloudflare script is not carried over;
  DNS stays with the provider and is documented in a page.
- **No diagrams**, generated or drawn.
- **No native mobile app.** The web application is responsive; that is all.

## 6. Product Components

### 6.1 CLI (priority 1)

The CLI is how agents and console-minded humans use hostingaffe, and it has to
be complete. Shape: `hostingaffe <object> <verb>`, like `gh`, `glab` and `pa`.
Short alias `ha`; `installation` may be shortened to `inst`.

```
ha machine add caddy --kind vps --provider hetzner --os "Ubuntu 26.04 LTS" \
   --cpu "2 vCPU" --memory 3.7G --disk 38G --ipv4 203.0.113.10 --ssh caddy
ha machine set caddy --os "Ubuntu 26.04 LTS" --measured-at 2026-09-05 --note "dist-upgrade"
ha machine view caddy --json           # the machine, every field, its installations and pages listed
ha machine context caddy               # everything an agent needs before touching this host
ha software add logaffe --repository https://github.com/datavisionzero/logaffe \
   --image ghcr.io/datavisionzero/logaffe
ha inst add logaffe-prod --machine caddy --software logaffe \
   --environment production --role application \
   --url https://logs.example.com --port 8080/tcp:internal --path /srv/logaffe \
   --backup active --monitoring external --logging local --description-file -
ha files put logaffe-prod compose.override.yml --file ./compose.override.yml
ha files put caddy-proxy sites/logaffe.caddy --file -
ha files list logaffe-prod             # path, size, revision, who, when
ha files sync /srv/logaffe --inst logaffe-prod  # on the machine: write the current files into place, show what changed
ha deploy logaffe-prod --version 1.4.0 --ref ghcr.io/datavisionzero/logaffe@sha256:… \
   --ticket LOG-42 --note-file -
ha deploy list --inst logaffe-prod     # the history, newest first
ha page add backup-restore --kind runbook --machine caddy \
   --title "Backup and restore" --body-file -
ha search "18502"                      # ports, addresses, names, Markdown, files — everything
ha export --dir ./hosting-export       # the whole record as a Markdown tree, the files, plus JSON
```

Commitments that matter to agents, inherited from planaffe and kept without
exception:

- **`--json` prints the complete object** wherever one object is printed.
  Lists are slim and paginated: a list of installations carries no Markdown
  bodies and no file contents.
- **`context` is the one call that spends context on purpose.** `ha machine
  context <key>` returns the machine, its installations with their current
  versions, ports and file list, the last deployments, the descriptions and
  pages that belong to it, and the instance-wide pages of kind `decision` —
  the rules that hold on every host, which an agent must know before its
  first command — as one Markdown document, ordered so that the part an agent
  needs first comes first. File *contents* are not in it; they are one `ha
  files get` away. An agent starting an SSH session on a host reads the
  context and nothing else (15.3).
- **Errors go to stderr, data to stdout.** Always.
- **Speaking exit codes.** Not found, refused, conflict, stale revision and
  unreachable are different numbers, so an agent can tell whether to retry.
- **An agent is configured by two environment variables** (`HOSTINGAFFE_URL`,
  `HOSTINGAFFE_TOKEN`) and nothing else, because that is how a harness hands a
  token to the thing it started. There is no per-repository project file,
  because there are no projects (9.).
- **A person signs in with `ha login`** and never puts a token in a shell
  profile. `ha` prints a short code, a person approves it in a browser on
  whatever machine has one, and the user token that comes back goes into the
  operating system's keychain — the only sign-in that works over SSH, in CI, in
  a container and in an agent's sandbox, where there is no browser on the
  machine doing the asking. Which instance and which token a command runs with
  are two ladders, and `ha status` prints both with the rung each came from:
  the instance is `--url`, then `HOSTINGAFFE_URL`, then the one this machine
  signed in to; the token is `HOSTINGAFFE_TOKEN`, then a token file chosen out
  loud, then the keychain. Where there is no keychain, `ha` says so and names
  the two ways on rather than quietly writing a credential to disk
  ([ADR 0005](docs/adr/0005-ha-login-is-the-device-code-flow-and-the-session-lives-in-the-keychain.md)).
- **A token never travels over plain HTTP off loopback.** `https://` always,
  `http://` to localhost for a development instance, and anything else refused
  before the request goes out unless somebody said `--insecure-http`
  ([ADR 0006](docs/adr/0006-a-token-never-travels-over-plain-http-off-loopback.md)).
- **Never interactive when stdin is not a terminal.** No editor, no prompt, no
  pager. Markdown and files arrive on stdin or from a path. `ha login` waits
  for an approval that happens elsewhere; it reads nothing.
- **Bulk writes in one call.** `ha machine add --file batch.json` creates a
  machine with its installations, software entries, files and first
  deployments in one transaction, because documenting a host is one act, not
  thirty commands. The file has the shape of the JSON that `ha export` writes
  (14.), so migrating a `hostaffe` repository has a defined target: one file
  the agent writes while it reads the old one. What an export carries back is
  the record and not the account of how it got there — the history, a file's
  earlier revisions and the original timestamps stay with the instance that
  wrote them, because the import is the ordinary acts and the history is the
  instance's to write (7.). Moving an instance is `pg_dump`, not this.
- **Every write can carry a `--note`.** The note lands in the history next to
  the change, so "why" is recorded where "what" is.
- **A write carries what it last read, and what that is depends on the
  record.** `ha files put` accepts `--revision`; with it, a write against a
  newer revision is refused with the stale-revision exit code, and without it
  the write wins and the history says so. Every write of a file prints the
  revision it produced. What has no revisions — a page, a machine, an
  installation — is guarded the same way with `--if-match` and the stand it
  was last read at: what has revisions is guarded with the revision, what has
  none with the stand. An agent that read before it writes passes what it
  read; one that puts a new file has nothing to pass.
- **`files sync` is the one command that touches the machine, and it pulls.**
  It runs on the host under the token of the agent whose SSH session it runs
  in — `HOSTINGAFFE_TOKEN` in that session's environment, and nowhere on the
  machine's disk (9.) — writes the installation's current files into the
  directory named, and prints what it wrote, changed or left alone. It knows
  what it wrote from a manifest it keeps in that directory, the one piece of
  state that lives outside Postgres: a file removed from the record is
  removed from the host by the next sync, because sync put it there, and a
  file sync did not write is never touched. It has `--dry-run`, and it never
  writes a secret because it never holds one.

### 6.2 Web interface (for humans)

The web interface follows planaffe's — the same shell, the same list density,
the same keyboard habits — because that interface is already good and our users
already know it. Its screens:

- **Machines** as the central list: name, kind, provider, OS, addresses, number
  of installations, when last measured. Sorted and filtered from the URL.
- **Machine** detail: the fields, the installations on it with their current
  version and ports, the machine-level files, the pages attached to it, the
  history.
- **Installation** detail: the fields, the current version and where it came
  from, the deployment history, the description rendered as Markdown, the
  files with their revisions, the pages.
- **File** view: the text, syntax-highlighted by extension, with its revisions
  and a diff between any two; editing it is guarded by the revision.
- **Software** detail: what it is, and every installation of it with its
  version — the screen that answers "where do I have to update Caddy?".
- **Deployments**: one timeline across the instance, newest first, filterable
  by machine, installation and software.
- **Pages**: runbooks, decisions and notes, by slug, with who touched what last.
- **Search** across every field, every Markdown body and every file.
- Every Markdown editor is a text area with a preview; every change to text or
  a file is guarded by a version so that two writers do not overwrite each
  other silently.
- Administration: users, agents and their tokens, personal settings.
- **Approving a `ha login`**: the one screen opened from a terminal rather than
  from the navigation. A person types the code `ha` printed and approves, and
  the machine at the other end collects a token of theirs (6.1).

There is no dashboard, no diagram, no chart.

### 6.3 HTTP API

One HTTP API under the CLI and the web interface, described by a checked-in
OpenAPI document. Nothing the web interface can do is missing from it.

## 7. Domain Model

Deliberately flat, and closed:

```
Instance
├── Machine            (the computer: VPS, dedicated, VM, local box)
│   ├── File           (host-level configuration: a systemd unit, an sshd snippet)
│   └── Installation   (one software installed once on one machine)
│       ├── Deployment (one version change — the history of the installation)
│       └── File       (what the installation runs with: Compose, Caddy fragment, script)
├── Software           (what an installation is an installation of)
├── Page               (Markdown, attached to a machine, an installation, or the instance)
├── History            (every change, written by the system)
└── Identity           (user or agent)
```

Two relationships are built in and no others: an installation is on a machine,
and a VM is on a host machine. A third — an installation depends on another
installation — is the first candidate for the roadmap (15.2), not a field in
the MVP.

Every entity has a **key**: a short, lowercase, immutable handle the operator
chooses — `caddy`, `ex44`, `docker-prod-01`, `logaffe-prod`. Keys are what
appear in commands, URLs, and Markdown links. They are unique per entity type
across the instance, not per parent: there is one machine `caddy` in the
instance, and naming it needs no parent. A machine `caddy` and a software
`caddy` may coexist, and usually will, because every command names the type
before the key. Where a key stands alone — a Markdown link, a search result, a
path in the export tree — it carries its type.

### The Machine

A machine is a computer the operator pays for or owns and can log into: a
rented VPS, a dedicated server, a virtual machine on one of those, or a box in
the office. It exists whether or not anything is installed on it.

| Field | Type | Notes |
| --- | --- | --- |
| `key` | handle | immutable |
| `name` | text | display name, defaults to the key |
| `hostname` | text | what `hostnamectl` reports, optional; the key is a handle, this is the machine's own name |
| `kind` | `vps` · `dedicated` · `vm` · `local` | closed set |
| `host` | machine key | only for `vm`: the machine it runs on |
| `provider` | text | `hetzner`, `netcup`, `home` — free text on purpose |
| `plan` | text | `EX44`, `CX22` |
| `location` | text | `fsn1-dc14` |
| `os` | text | `Ubuntu 26.04 LTS` |
| `arch` | `amd64` · `arm64` | closed set; the one hardware fact an agent compares, because the image it pulls depends on it |
| `cpu` | text | `2 vCPU`, `Intel i5-13500` |
| `memory` | text | `3.7G` |
| `disk` | text | `38G`, `2×512G NVMe ZFS mirror` |
| `ipv4`, `ipv6` | address | public addresses, each optional |
| `private_ip` | address | on the operator's private network, optional |
| `ssh` | text | the SSH target or alias the operator uses |
| `status` | `planned` · `active` · `retired` | closed set |
| `measured_at` | date | when the facts above were last verified against the machine |
| `description` | Markdown | what the machine is for, and anything that fits nowhere else |

Hardware fields are text, not numbers, with `arch` as the one exception. We
want to compare machines by eye, not sum their memory, and "2×512G NVMe ZFS
mirror" is a truer description than a number.

`measured_at` is the answer to the stale-state problem in section 2: the
machine list shows it, and a machine nobody has looked at for a year says so.

**Deliberately left out:** rack, serial number, warranty, purchase date, owner,
tags, monthly cost (17.), a free-form key/value bag.

### The Software

Software is the thing an installation is an installation of — `caddy`,
`postgres`, `logaffe`, `uptime-kuma`. It exists once per instance, so that the
question "where is this running, and in which versions?" has a screen.

| Field | Type | Notes |
| --- | --- | --- |
| `key` | handle | immutable |
| `name` | text | |
| `homepage` | URL | optional |
| `repository` | URL | the upstream source, optional |
| `image` | text | the container image name without a tag, optional |
| `description` | Markdown | what it is, and how we use it in general |

A software entry is created the first time it is needed, usually in the same
call as the installation that needs it. It carries no version: versions belong
to deployments.

### The Installation

An installation is one software installed once on one machine. Two logaffe
installations on the same host are two installations. Caddy on three hosts is
three installations of one software. The word is chosen over "service", which
means something else to systemd and to Docker Compose.

| Field | Type | Notes |
| --- | --- | --- |
| `key` | handle | immutable, `logaffe-prod`, `caddy-proxy` |
| `name` | text | |
| `machine` | machine key | required |
| `software` | software key | required |
| `environment` | `production` · `staging` · `development` | closed set; whom it serves |
| `role` | `application` · `platform` | closed set; `platform` is what the host runs for everyone — Caddy, Uptime Kuma, Beszel, ntfy — the template's "host service" |
| `status` | `planned` · `active` · `retired` | |
| `urls` | list of URL | where it is reachable, if anywhere |
| `ports` | list of objects | `{ "port": 443, "protocol": "tcp", "scope": "public" }`; `protocol` is `tcp` · `udp`, and scope is `public`, `private` (the operator's network) or `internal` (a Docker network) |
| `path` | text | where it lives on the machine, `/srv/logaffe` — also where `files sync` writes by default |
| `data` | text | where its persistent data lies, `/srv/services/logaffe` — what a backup has to take |
| `secrets` | list of objects | `{ "name": "POSTGRES_PASSWORD", "path": "/opt/compose/logaffe/.env.runtime" }`; the *name* it needs and the file the value lies in, never the value. `path` may be empty, and reads as `NAME@/the/file` ([ADR 0011](docs/adr/0011-a-secret-is-a-row-that-says-which-file-it-lies-in.md)) |
| `backup` | `none` · `planned` · `active` | the decision, as in the template |
| `monitoring` | `none` · `planned` · `external` | the decision, like the backup: `planned` is what was deferred on purpose, `none` what nobody decided |
| `logging` | `local` · `central` | |
| `version` | derived | the `version` of its latest deployment by `at` |
| `description` | Markdown | the runbook: how it is deployed, checked, updated, rolled back, what its data is |

Environment and role are two fields because they answer two questions: whom an
installation serves, and what it is for the host. A Caddy that fronts
production and staging alike is `platform` and `production`: it serves real
traffic, and its ACME data is production data. "Every production installation
without a backup" must find it. One field with a third value `infrastructure`
was considered and rejected, because it would have made Caddy neither.
`development` is for an installation that serves nobody but the operator — a
test instance on the box at home. The home automation on that same box serves
the household, and is `production`.

A port is an object and not the string `443/tcp:public`. That spelling is a
*rendering* — it is what the CLI and the interface show and take, and what a
history row carries, because a person reads those. As a field it would have been
the one value in the model with a grammar of its own: the check would move into a
regular expression, "every installation with a public port" would become a text
search instead of a query, and the generated clients would see a `string` they
can read nothing out of. `protocol` and `scope` are closed sets like every other
one here, with the same check constraint in the column.

**An installation has two directories, and both are fields.** The one it is
deployed from holds the Compose file and the runtime environment; the one its
data lies in holds the database directories and everything else a `docker
compose down -v` does not bring back. The separation of configuration and state
is the usual one, and on the first host migrated it is `/opt/compose/<service>`
and `/srv/services/<service>`. A record that keeps the first in `path` and the
second in the description has the fact that decides every backup as prose
([ADR 0009](docs/adr/0009-an-installation-has-two-directories-and-data-is-the-second.md)).
Where a host keeps both in one directory, both fields say it, and nothing holds
them apart: two directories is what the model allows, not what it demands.

The three decision fields — backup, monitoring, logging — are fields rather
than prose because they are the ones we want to *list*: "every production
installation without a backup" is a question the product must answer in one
line. Backup and monitoring each carry a `planned` for the same reason: a
decision that was taken and deferred, left in the same bucket as the one nobody
ever took, makes that list answer the wrong question.

The description is created from a fixed template with the headings our
service files already have (deployment, health check, persistent data, update
and rollback, the reasoning behind the three decisions). The template is a
starting point, not a schema: an agent may delete a heading that does not
apply.

**Deliberately left out:** criticality, acceptable downtime, acceptable data
loss, owner, external dependencies (15.2), a `logaffe` project reference and a
`vaultaffe` reference (15.4).

### The File

A file is a UTF-8 text file that a machine runs with, kept next to the
installation — or the machine — it belongs to: a `compose.override.yml`, a
Caddy site fragment, a systemd unit and its timer, a `bin/` script, an
`.envrc`. It is what the derived `hostaffe` repositories keep under
`stacks/<service>` and what their deploy script copies to the host.

| Field | Type | Notes |
| --- | --- | --- |
| `owner` | installation key or machine key | exactly one |
| `path` | relative path | `compose.override.yml`, `sites/logaffe.caddy`, `bin/logaffe-stack`; unique per owner |
| `directory` | absolute path | where it lies on the machine — `/etc/systemd/system`. A machine's file has one, an installation's has none (ADR 0008) |
| `executable` | boolean | so that a script arrives runnable |
| `content` | text | UTF-8, capped at one megabyte |
| `revision` | derived | one per write; the history keeps every previous content |

Files are the one place where the history keeps *content*, not just the fact of
a change: rolling back a Compose file needs the previous Compose file. Two
revisions can be diffed in the interface and in the CLI.

Two kinds of path are refused outright: the ones the template already declares
secret-bearing — `.env` and any `.env.*` except `.env.example`, and anything
under `secrets/` — and anything outside the installation's directory (`..`,
absolute paths). This is the one list; section 10 refers to it. `.envrc`
itself is welcome: in the template it is one line and carries no value. The
CLI also warns when content looks like a private key or a token. That is a
guard against accidents, not a security boundary: the operator's secrets live
in vaultaffe or on the host, never here.

An installation has one directory for every file it owns, and it is the
installation's own `path`. A machine has none, so each of its files says which
one it lies in: sixteen units under `/etc/systemd/system`, eleven scripts under
`/usr/local/sbin`, a `daemon.json` under `/etc/docker` is what one real host
turned out to hold, and a record that cannot tell them apart holds texts nobody
can put back.

The files of an installation are a set, and `files sync` writes the whole set.
There is no partial deployment of files, no per-file "deployed" flag, and no
record on the server of what a machine holds: the machine holds what the last
sync wrote — the manifest sync keeps beside the files (6.1) is the machine's
record, not the server's — and the deployment record (below) says which
revisions were current when a version went live.

**`files sync` writes where the directory belongs to what is written into it.**
That is one rule and not two, and the difference between the two owners follows
from it rather than being an exception to a principle. An installation owns a
directory: `path` is one line in the record, the directory it is deployed from,
and what lies there is its files. So sync writes the whole set into it and
clears away what has left the record, and the worst it can reach is the
installation's own. A machine owns none. Its files lie under
`/etc/systemd/system`, `/usr/local/sbin`, `/etc/docker` — roots that belong to
the operating system and hold a thousand things this record has never heard of
— and a sync that removed from them would be removing somebody else's. The
record is not shy of the disk: 13. says the product writes files into a
directory and stops there. It is shy of a directory that is not its to sweep.
So a machine's directory is written down, not written to: the agent acts on the
machine and the record says what is there
([ADR 0008](docs/adr/0008-a-machines-file-says-where-it-lies-and-is-never-synced.md),
[ADR 0013](docs/adr/0013-sync-stays-because-a-directory-has-an-owner.md)).

**A file has one owner even where two installations care about it.** A host with
one reverse proxy in front of everything is the ordinary way to run one, and
`sites/logaffe.caddy` lies under the proxy's `path`: it is the proxy
installation's file, the proxy's sync writes it, and nothing on it names the
installation it fronts. That installation says so in its description instead —
the path, and `[caddy](installation:caddy)` — the way the `hostaffe` template
already says it under **Network** in every service's own document. A second
owner would be a claim on a directory that is not the service's, and a field
naming what a file *concerns* would be 15.2 entered through the side door, with
the wrong cardinality: a fragment can front two installations and a
`daemon.json` concerns them all
([ADR 0010](docs/adr/0010-a-file-has-one-owner-and-what-else-it-concerns-is-prose.md)).

**Deliberately left out:** directories as objects, binary content, symlinks,
ownership and mode beyond the executable bit, templates or variable
substitution inside files, and a second installation a file refers to (ADR
0010).

### The Deployment

A deployment is the record that an installation changed version. It is the
history of the installation and the source of its current version.

| Field | Type | Notes |
| --- | --- | --- |
| `installation` | installation key | |
| `version` | text | what runs afterwards: `2.11.4`, `1.4.0`, `main-20260905` |
| `previous` | derived | the version of the deployment before it |
| `ref` | text | the exact thing that was deployed — an image reference with digest, a git SHA, a tag; optional but wanted |
| `files` | derived | the revisions of the installation's files current at `at`; empty for a deployment backfilled to before the first file was put |
| `at` | timestamp | when the version went live; defaults to now, settable, so that history can be backfilled |
| `by` | identity | who recorded it, written by the system — for a backfilled deployment that is not necessarily who deployed |
| `ticket` | text | a planaffe key like `LOG-42`, optional |
| `note` | Markdown | why, what was checked, what went wrong |

There is no status. A deployment is recorded when it is done. A rollback is a
deployment to the previous version with a note that says so; a failed attempt
that changed nothing is a note on the installation or a ticket, not a record
here. That keeps the list honest: every row is a version that actually ran.

The first deployment of an installation is created with the installation, so an
installation never has a version without a record of when it appeared. The
latest deployment is the latest by `at`, not by the order of recording, so
that backfilling history never changes the current version.

A deployment has no key; the system numbers it, and the number is what `ha
deploy view`, `ha deploy set` and the interface use. An agent will record a
wrong one, so the rule for correcting is fixed: `ref`, `ticket`, `note` and
`at` can be changed, and the history records the change; `version` and
`installation` cannot, because they are what the record *is* — a deployment
with the wrong version is deleted (below) and recorded again.

### The Page

A page is Markdown with a slug, attached to a machine, an installation, or to
the instance as a whole. It is where everything goes that is longer than a
description: the backup runbook of a host, the decision to use Tailscale for
management, the notes from an incident.

| Field | Type | Notes |
| --- | --- | --- |
| `slug` | handle | `backup-restore`, `tailscale-for-management` |
| `title` | text | |
| `kind` | `runbook` · `decision` · `note` | closed set |
| `attached_to` | machine key, installation key, or nothing | |
| `body` | Markdown | |

Pages are flat, addressed by slug, and edited like planaffe's pages. Flat is
meant literally: a slug is one segment and carries no slash, so
`/api/pages/{slug}/history` can never be read as a page of its own
([ADR 0003](docs/adr/0003-a-pages-slug-is-one-segment-not-a-path.md)). A page
names what it belongs to in `attached_to`, not in its address. A `decision` is
a page whose kind says it should be read as one; there is no status, no
supersedes, no template enforcement beyond the kind.

A body names another thing of the record as an ordinary Markdown link whose
target is a scheme and an address — `[the runbook](page:backup-restore)`, and
likewise `machine:ex44`, `software:caddy` and `installation:app-1`
([ADR 0007](docs/adr/0007-a-record-is-linked-from-markdown-as-a-scheme-and-a-key.md)).
The scheme carries the type because the address does not, which is the same
rule a key follows everywhere else it stands alone. Nothing validates a body:
the instance stores Markdown and does not parse it, so a reference that points
at nothing is stored like any other text, and `ha page check` says so.

**Deliberately left out:** folders, page hierarchy, attachments, images.

### The History

Every change to a machine, software, installation, file or page is recorded:
who, when, which field, from which value to which, and the note that came with
the change. Written by the system, not editable, not deletable. Text fields
record that they changed, not the diff; files are the exception and keep their
content.

Deployments are not history entries; they are records of their own, because
they are the thing the operator wants to *read*, while the history is what
they consult when something looks wrong.

### Retiring and deleting

`retired` is the normal end of a machine or an installation. A retired thing
keeps its installations, files, deployments and history exactly as they were,
so that "what did we run in 2026" stays answerable; it leaves the default
lists and the machine context, and stays reachable by key, in search, and in
the deployment timeline.

Deleting is for mistakes — a machine created twice, an installation that never
existed, a deployment recorded with the wrong version. It follows planaffe's
rule ([ADR 0013](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0013-deleting-is-a-soft-delete-with-a-floor-and-identities-are-never-deleted.md)):
a soft delete, invisible everywhere at once, restorable for a grace period,
removed for good afterwards, and open to agents because the grace period is
the safety net. Deleting a machine deletes its installations, files and
deployments with it; deleting a software that still has installations is
refused. The history is never deleted, not even with the thing it describes:
the history of a deleted machine still says that it existed and when it was
deleted. A deleted key is never reused. Identities are deactivated and
revoked, never deleted.

## 8. What an Agent Does Here

The cycle the product is built around, in the order it happens:

1. **Before touching a host**, the agent runs `ha machine context <key>` and
   has, in one document, what the machine is, what is installed on it, which
   versions, which ports, where the data is, which files each installation
   runs with, how each is updated, and which decisions apply. No repository to
   clone, no six files to read.
2. **While working**, the agent may look things up by key or by search — a
   port, a path, a secret and the file it lies in, a file — with one command
   each.
3. **Changing configuration** is `ha files put`, then `ha files sync` on the
   host, then whatever the runbook says — `docker compose up -d --wait`, a
   Caddy reload. The product wrote the file and remembers the revision; the
   agent runs the command and remembers to record the result.
4. **After the work**, the agent records what it did: `ha deploy` for a version
   change, `ha inst set` or `ha machine set` with a `--note` for anything else,
   `ha page add` or `ha page set` for a runbook it wrote or corrected. The human reviews in the
   web interface, and corrects where the agent was wrong.
5. **Setting up a new host** is one bulk call from a file the agent writes
   while it measures the machine, followed by the files and pages it writes
   while it configures it.

The `AGENTS.md` block a user pastes into their repository (`docs/agents-md.md`,
as in planaffe) says exactly this, in the handful of commands it needs.

## 9. Users and Permissions

The identity model is planaffe's, minus the project dimension:

- **Users** for humans, **agent tokens** for agents, **user tokens** as a
  human's key to the CLI. Everything written is bound to an identity, so the
  history, every deployment and every file revision say who.
- **Every user sees everything.** One instance holds one team's infrastructure.
  There are no projects, no per-machine access, no roles beyond the
  administrator who manages users, agents and tokens.
- **An agent token is the agent.** It has a name, it is never an
  administrator, and an agent cannot create or read tokens. Every token reads
  everything and writes what it is told to.
- **No token lives on a machine.** `files sync` runs under the token of the
  agent that opened the SSH session, handed in as `HOSTINGAFFE_TOKEN` for that
  session and written to no file on the host. A token stored on a machine
  would hand whoever takes that machine the map of every other one, and a
  read-only token would not change that — it still reads everything. A token
  scoped to one machine is the roadmap answer (17.).
- Sign-in, browser sessions, invitation and password recovery are what planaffe
  has, built the same way. What is this product's own is the **device login**:
  `ha login` prints a code, a user approves it at `/device` in a browser, and
  the machine that asked collects an ordinary user token — revocable in
  `ha token list` and in the browser like any other (ADR 0005). A person's
  token then lives in that machine's keychain and not in a file; an agent's
  still arrives in `HOSTINGAFFE_TOKEN` and nowhere else. A user is invited by e-mail rather than created with
  a password somebody has to hand over, so no password ever travels through a
  third person.
- **Transactional e-mail is an optional instance capability** (planaffe
  ADR 0018). It is configured with the SMTP variables of the environment, and
  an instance without them is a working instance: the first administrator
  arrives through the bootstrap path, which sends nothing, and the acts that
  cannot happen without a mail — inviting a user, resending an invitation,
  password recovery, changing an e-mail address — refuse with
  `smtp-not-configured` and say why. An operator can ask the instance whether
  it is configured and have it send one test mail. Optional because an instance
  can be one person and their agents, and a mail server nobody needs is a
  dependency nobody should have to install; a team of humans configures it once
  and then never thinks about it.

## 10. Reachable From the Internet, and What That Means Here

Like its siblings, a hostingaffe instance is meant to run on a public host over
HTTPS and be sound there without a VPN in front of it. This product is the one
of the family where that matters most: its content is a map of the operator's
attack surface, configuration included. Consequences:

- Nothing is readable without a session or a token. No public pages, no
  anonymous read of anything, no "share link".
- Sign-in is rate-limited, sessions are server-side and revocable, tokens are
  stored hashed.
- The product refuses to store what the template already declares secret: a
  secret *name* may not contain `=`, `:` or whitespace, the file it lies in is
  a path and not a value, a file may not have one of the paths section 7
  refuses, and the CLI warns when Markdown or a file on stdin contains
  something that looks like a private key or a token. That is a guard against
  accidents, not a security boundary.
- No token is stored on a machine (9.). The one command that runs on a host
  borrows the session's token and leaves nothing behind.

## 11. What It Is Next To

hostingaffe is one of four products with one author, one house style, and one
target group. Each does one thing:

| | keeps | for the agent through |
| --- | --- | --- |
| planaffe | tickets: what is to be done | `pa` |
| logaffe | logs: what the applications said | MCP |
| vaultaffe | secrets: what the applications need to run | `vaultaffe` |
| **hostingaffe** | **the machines: what runs where, since when, with which files, and how** | **`ha`** |

They stay separate products, and hostingaffe depends on none of them at
runtime. Where they touch is by reference, and the references are cheap:

- A deployment names a **planaffe** ticket, and a page or a note may link one.
- An installation may name its **logaffe** project and a machine its logaffe
  host, so that the installation screen links to its logs. The other
  direction — logaffe's host list seeded from here — is a change to logaffe,
  and later (15.4).
- An installation's secret names may point to a **vaultaffe** project and
  environment, so an agent knows where to get them without asking — beside
  the file on the machine each one already names. Values never cross.
- hostingaffe logs into logaffe through the same Serilog sink planaffe uses.

What hostingaffe replaces: the `hostaffe` template, the private per-machine
repositories derived from it, and their deploy script. What it does not
replace: the product repositories, whose published Compose files an
installation's override file composes *with*, exactly as the derived
repositories do today. The product ships the file that works for a stranger;
hostingaffe keeps the few lines that make it ours.

## 12. Technical Guard Rails

The stack is planaffe's, adopted rather than re-decided; the ADRs that chose it
are planaffe's and are referenced, not rewritten.

- **Backend:** .NET 10 in four layers — Domain, Application, Infrastructure,
  Api — with dependencies pointing inward and the Domain carrying no packages.
- **Storage:** PostgreSQL, all content included, file revisions too; EF Core
  owns the schema and the migrations, which apply themselves on startup and
  only ever go forward. Full-text search through Postgres, no separate index.
- **Frontend:** React on Vite, Tailwind and Base UI, `react-markdown` — the
  planaffe shell (project-less), list, detail and settings patterns copied
  into this repository.
- **Copied, not shared.** The shell, the identity model and the CLI skeleton
  are copied from planaffe, not extracted into packages. Two copies cost a fix
  applied twice; a shared package would cost a release process and a
  dependency between products that are deliberately separate. Extraction is
  reconsidered when a third product needs the same parts.
- **CLI:** Go, one static binary, `ha`; exit codes, `--json`, stdin bodies and
  the non-interactive rule exactly as `pa` has them; `User-Agent` and a server
  version header so that skew is refused rather than guessed at. `files sync`
  is in the same binary, so a machine needs one download and one token.
- **API:** one HTTP API with an OpenAPI document captured from a running
  instance and checked in; the web application's client is generated from it.
- **Identity:** planaffe's users, agents, tokens, sessions, invitations and
  recovery, with the same SMTP sender behind the three mails they need.
- **Deployment:** one image, one Compose file, Postgres beside it, port
  `8080`; upgrade is `docker compose pull && up`; backup is `pg_dump`.
- **Logs of the instance itself:** Serilog to console and file, to logaffe when
  configured.
- **Repository conventions:** `CONTEXT.md` as the glossary the code is named
  after, `docs/adr/` for decisions, `docs/codebase.md`, `docs/storage.md`,
  `docs/api.md`, `docs/cli.md`, `docs/human-interface.md`, `docs/install.md`
  written for an agent to execute, `docs/agents-md.md`, `deploy/` with the
  Dockerfile and Compose and nothing else, `scratchpad/` ignored. Everything in
  the repository is English. `CLAUDE.md` is a symlink to `AGENTS.md`.

## 13. Where Structure Stops

Three lines are drawn on purpose, and all three will be pushed against.

**Fields end where evaluation ends.** A field exists because the product
filters, lists, compares or derives from it. "Acceptable downtime" is a real
question, and it is prose in the description, because nothing in the product
would ever act on it. The test for adding a field is: name the list or the
comparison it makes possible. If there is none, it is Markdown.

**Markdown ends where the machine executes.** A description quotes the update
commands and explains them; the Compose file the host actually runs is a file
with a path and a revision, not a code block in prose. The two are next to
each other on the installation, and the deployment says which revision of the
file went live with which version.

**Files end where execution begins.** hostingaffe writes files into a
directory when asked and stops there. It does not run them, does not template
them, does not know whether the container came up. The runbook says what to
run next, and the agent runs it. Templating and variable substitution inside
files are the first thing someone will ask for, and the answer is that a file
is what the host runs, verbatim, so that what is stored is what is true.

## 14. MVP Scope

**Included:**

- Machines, software, installations, deployments, files, pages, the history,
  the closed field sets of section 7, and the fixed description template for
  installations.
- The complete CLI: add, set, view, list, delete (soft, 7.) for every entity,
  `deploy`, `files put`, `files get`, `files list`, `files diff`, `files sync`,
  `search`, `context`, bulk create from a file, `export`.
- The web interface of 6.2, built on the planaffe shell.
- Users, agents, tokens, sessions; invitation and recovery as in planaffe.
- Container image, Compose file, self-applying migrations, `docs/install.md`.
- Export as a Markdown tree with the files in place, plus JSON — the escape
  hatch that makes adopting the product safe, and a tree that looks like the
  repositories it replaces. The JSON is the bulk-import format (6.1), so an
  export can be read back into an instance as the record it describes,
  beginning there (6.1).

**Not included (deliberately deferred):**

- Assisted measurement of a machine from the machine itself (15.1).
- Dependencies between installations (15.2).
- Live references into logaffe and vaultaffe beyond a link (15.4).
- An MCP server (15.5).
- Import from an existing `hostaffe`-style repository as a feature: the
  agent does that with the bulk call and `files put`, and the first thing we
  do with the MVP is migrate our own repositories that way.
- Cost, contracts, warranties; diagrams; attachments; page hierarchy; DNS.

## 15. Roadmap After the MVP

### 15.1 Measuring instead of typing

`ha machine facts`, run *on* the machine, collects what `current-state.md`
asks for today — `hostnamectl`, `lscpu`, `free`, `df`, the Docker versions,
the listening ports — and prints JSON that `ha machine set <key> --facts -`
accepts for `hostname`, `arch`, `os`, `cpu`, `memory`, `disk` and the
addresses. Nothing is discovered unasked; the agent runs one command instead of
seven and copies nothing by hand. The same output compared against the record
is the cheapest possible drift check: "the machine says Docker 29.7.2, the
record says 29.1.2, measured 40 days ago." The same idea applied to files —
`ha files sync --check` reporting where the directory on the host differs from
the record — is the drift check for configuration.

### 15.2 An installation depends on an installation

Caddy on `ingress-01` routes to payaffe on `docker-stage-01`; payaffe uses the
Postgres beside it. A `depends_on` list of installation keys would let the
machine screen say what breaks when the machine goes down, across machines. It
is the one relationship worth adding, and it is deferred only because the MVP
should first show whether the two built-in ones carry the everyday questions.

### 15.3 The context package, tuned

`ha machine context` is the command that decides whether an agent works well
here. After the MVP it gets what planaffe's ticket-as-context-package idea
gets: an order that puts the runbook before the hardware, a `--brief` that
leaves the page bodies out, a `--files` that includes file contents for the
run that needs them, and a size the operator can see so that a host whose
documentation has grown past what a session should read is visible as such.

### 15.4 Talking to the siblings

An installation's `logaffe` project and a machine's logaffe host, as fields
with a link; an installation's secrets resolved against a vaultaffe project so
that `vaultaffe run` and `ha inst view` agree on the list, and so that where a
value is kept off the machine becomes a reference rather than prose
([ADR 0011](docs/adr/0011-a-secret-is-a-row-that-says-which-file-it-lies-in.md));
logaffe's host list and collectors seeded from hostingaffe's machines. Each of
these is partly a change to the other product and is decided there.

### 15.5 MCP

For agents whose harness reaches tools through MCP rather than a shell, the
same read operations — context, view, search, files get — as an MCP server
beside the API. Reads first; whether writes belong there is decided when it is
built.

### 15.6 Further ideas in the same direction

- A monthly cost on the machine, because the machine list is the one place an
  operator would total it (17.).
- An export shaped for agents — one Markdown index over the whole instance in
  the spirit of `llms.txt` — for a harness that prefers reading a file to
  calling a CLI.
- A token scoped to one machine, for a host that syncs its own files without
  an agent's session (17.).

## 16. How We Measure Success

- From `git clone` to a running instance with the first machine recorded in
  under ten minutes, by an agent following `docs/install.md`.
- Our own six machines migrated out of their Markdown repositories into one
  instance by an agent — fields, pages and files — in one working session
  each, and the repositories archived afterwards.
- An agent given only `ha machine context caddy` can update an installation on
  that host, sync its files, and record the deployment without asking the
  human anything the record should have contained.
- "Every production installation without a backup", "which versions of Caddy
  do we run", "what listens on 18502", "which Compose file went live with
  1.4.0" — each is one command and one screen.
- The context of a typical machine fits comfortably in a session: well under
  ten thousand tokens for a host with five installations, file contents
  excluded.
- The README explains the product in one screenful.

## 17. Open Points

- **Machine kinds.** Is `local` enough for a NUC, a Mac, a Raspberry Pi, or
  does a homelab want `desktop` and `sbc`? Closed set either way.
- **Versions.** Text, or a parsed version for sorting and "newer than"? Text
  until sorting is actually needed.
- **Machine-scoped tokens.** In the MVP no token lives on a machine (9.), so a
  host cannot sync its own files without an agent's session. A token that
  reads one machine and writes nothing would allow that without turning a
  compromised host into a map of the others. A merely read-only token would
  not — it still reads everything. Whether the scoped kind is worth its
  explanation is decided after the MVP.
- **Cost.** A single `monthly_cost` on the machine is cheap and often asked
  for, and it is the first step onto a slope (currency, billing period,
  contracts). Deferred, not refused.
- **Instance-wide pages** versus a `general` machine key: a page attached to
  nothing is simple, but "where do instance-wide decisions live" should have
  one answer in the interface.
