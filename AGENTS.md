# hostingaffe

Instructions for coding agents working in this repository. See
[Vision.md](Vision.md) for what hostingaffe is and what it deliberately is not.

The repository is **pre-MVP**: the foundation stands — a copy of planaffe
with planaffe's domain cut out — and this product's own domain is not built
yet.

Of the files [Vision.md §12](Vision.md#12-technical-guard-rails) names as
conventions, [`CONTEXT.md`](CONTEXT.md), [`docs/codebase.md`](docs/codebase.md),
[`docs/cli.md`](docs/cli.md), [`docs/install.md`](docs/install.md),
[`docs/operations.md`](docs/operations.md), [`docs/api.md`](docs/api.md),
[`docs/storage.md`](docs/storage.md), [`docs/adr/`](docs/adr/) and
[`deploy/`](deploy/) all exist. A new one is linked here when it is created.

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

## The scratchpad

`scratchpad/` is the local working area — notes, drafts, throwaway experiments.
It is in `.gitignore` and never reaches the remote. If the directory does not
exist, proceed silently; it is not part of the published repository.

The scratchpad keeps the working level out of the public repository. Where a
maintainer's own working items live is their business and is not described
here. **GitHub issues are where things are reported and discussed in the
open**, and they are English like everything else that reaches the remote.
