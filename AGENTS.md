# hostingaffe

Instructions for coding agents working in this repository. See
[Vision.md](Vision.md) for what hostingaffe is and what it deliberately is not.

The repository is **released**: `v0.1.0` is out, and the domain — machine,
software, installation, deployment, file, page, history, identity — stands
behind the API, the web application and `ha`. What that changes for work here
is that `main` is now something strangers pull: an image tagged `:latest` is
what a `docker compose up` reaches for, and the release is cut from a tag by
[`.github/workflows/release.yml`](.github/workflows/release.yml).

Of the files [Vision.md §12](Vision.md#12-technical-guard-rails) names as
conventions, [`CONTEXT.md`](CONTEXT.md), [`docs/codebase.md`](docs/codebase.md),
[`docs/cli.md`](docs/cli.md), [`docs/install.md`](docs/install.md),
[`docs/operations.md`](docs/operations.md), [`docs/api.md`](docs/api.md),
[`docs/storage.md`](docs/storage.md), [`docs/agents-md.md`](docs/agents-md.md),
[`docs/adr/`](docs/adr/) and [`deploy/`](deploy/) all exist. A new one is linked
here when it is created.

[`docs/agents-md.md`](docs/agents-md.md) is the odd one out: it is not about
working *in* this repository but about working on a host whose record lives in
a hostingaffe instance. It holds the block a user copies into the `AGENTS.md`
of their own repository ([Vision §8](Vision.md#8-what-an-agent-does-here)), and
every command in it has to keep working — a block that names a verb `ha` no
longer has is worse than no block.

## Language

Everything in the repository is written in **English**, regardless of the
language a contributor speaks: source code, identifiers, comments, docs, ADRs,
commit messages, PR titles and bodies, and issues.

Working notes and drafts under `scratchpad/` are exempt — they are local and
never pushed (see below).

## Repository and contributions

- **Host**: GitHub — `datavisionzero/hostingaffe`. Public, MIT — all of it,
  with no directory and no feature behind a second license.
- **Layout**: [`docs/codebase.md`](docs/codebase.md) — what is where, and why.
  It is kept current: a file that lands somewhere other than it describes means
  one of the two is wrong.
- **Language of the domain**: [`CONTEXT.md`](CONTEXT.md) is the glossary the
  code is named after, and the model is closed — machine, software,
  installation, deployment, file, page, history, identity, each named by an
  immutable **key**. Code, identifiers, the HTTP contract and the CLI use those
  words without exception, and so does the list of words that are *not* used.
  A concept that needs a word the glossary does not have gets settled in
  [Vision.md §7](Vision.md#7-domain-model) first.
- **Decisions**: the stack is planaffe's, adopted rather than re-decided, and
  the ADRs that chose it live in `datavisionzero/planaffe` — reference them,
  do not rewrite them; [`docs/adr/README.md`](docs/adr/README.md) lists the
  adopted ones. Decisions this product makes for itself go in
  [`docs/adr/`](docs/adr/), starting with
  [0001](docs/adr/0001-the-foundation-is-a-copy-of-planaffe.md), which records
  that the foundation is a copy. Say so explicitly when your work contradicts
  one instead of silently overriding it.
- **Research**: [`docs/research/`](docs/research/) holds the reading behind the
  vision — what already exists in this space and why hostingaffe is not it.
  Read the relevant file before re-arguing a boundary the vision has drawn.
- Contributions arrive as pull requests from forks. Maintainers may push to
  `main` directly.
- Commit and push only when asked to.

## Branching

The repository is a **trunk**: `main` is the only long-lived branch, and it is
always in a state that could be released.

- Committing straight to `main` is the normal path for maintainers.
- A short-lived branch is optional — take one when the work is large, risky, or
  wants review, and merge it back within days, not weeks.
- Whatever the path, CI has to be green on `main` once there is any. A red
  trunk is fixed or reverted before anything else is pushed on top of it.

## Before pushing

Two checks, every time, because a public repository does not forget — and this
product's subject matter makes the first one sharper than usual.

1. **No personal information, and no real infrastructure.** No real names,
   private e-mail addresses, home or IP addresses, hostnames of private
   machines, absolute paths carrying a user name, or anything else that
   identifies a person. hostingaffe is a record of machines somebody rents or
   owns: an example, a fixture, a screenshot or a piece of documentation uses
   invented hosts, invented domains and documentation address ranges, never the
   maintainers' own. Commit authorship and `datavisionzero` are the exception —
   that is the account this is published under. Check the diff, not just the
   files you meant to change: `git diff --staged` and `git log -p @{u}..`
   before the push.
2. **No secrets.** No tokens, connection strings, private keys or `.env`
   contents, not even expired or example ones that look real.

When something has to be written down that fails either check, it belongs in
`scratchpad/`, which is ignored by git.

## Releasing

A **tag is the only thing that makes a version**. Every build nobody tagged
calls itself `0.0.0-dev` — the .NET side from `Directory.Build.props`, `ha`
from `internal/version` — and
[`.github/workflows/release.yml`](.github/workflows/release.yml) is the one
place either is told otherwise.

```sh
git tag -a v1.2.3 -m "..."   # annotated, and the message is the release's
git push origin v1.2.3
```

- **The shape is `v1.2.3`**, or `v1.2.3-rc.1` for a prerelease. Anything else
  the workflow refuses, because a typo must not become `:latest`.
- **CI has to be green on the commit the tag names.** The workflow checks that
  rather than re-testing, and refuses a commit the gate never passed. Tag the
  trunk after its run went green, not before.
- **A stable release moves `:latest` and `:1.2`; a prerelease moves neither**
  and is not what `releases/latest/download/` resolves to. That is what lets
  [`docs/install.md`](docs/install.md) and [`docs/cli.md`](docs/cli.md) print
  URLs with no version in them.
- What goes out is one multi-architecture image on GHCR, `ha` for five
  platforms with a `checksums.txt`, and a GitHub release. A run that failed
  halfway is repeated with `workflow_dispatch` and the same tag — the tag is
  not deleted and re-pushed, because people may already hold it.

Since the first release, `main` is something strangers pull. A change to the
Compose file, to `deploy/.env.example` or to the variables the instance reads
is a change to an installation somebody already runs: it either keeps working
untouched on `docker compose pull && up -d`, or the release notes say what to
do, and migrations only ever run forward.

## The scratchpad

`scratchpad/` is the local working area — notes, drafts, throwaway experiments.
It is in `.gitignore` and never reaches the remote. If the directory does not
exist, proceed silently; it is not part of the published repository.

The scratchpad keeps the working level out of the public repository. Where a
maintainer's own working items live is their business and is not described
here. **GitHub issues are where things are reported and discussed in the
open**, and they are English like everything else that reaches the remote.
