# The Foundation Is a Copy of planaffe

hostingaffe started by copying planaffe's tree whole — the four .NET projects,
the Go CLI, the React application, the tests, `deploy/`, the workflows — then
renaming it and cutting planaffe's domain out of the copy. It was not
assembled file by file from the parts that looked reusable, and it is not
going to be extracted into anything shared.

The obvious alternative was to start empty and take what was needed. It was
not taken because of the ratio: half of planaffe is not its ticket model. The
identity model with its users, agents, tokens, browser sessions, invitation
and recovery; the page with its slug; the history; the soft delete with its
grace period and its purge; the problem documents and the exit codes derived
from them; the OpenAPI document captured from a running instance with both
clients generated from it; the application shell; the Dockerfile, the Compose
files and the release chain — all of that is solved work that hostingaffe
needs unchanged. Copying it and cutting is one diff a reviewer can read.
Gathering it is a hundred decisions about what to take, each made without the
code around it, and every one of them a chance to leave a part of a mechanism
behind.

The order matters and was deliberate: copy whole, rename, then cut. Three
commits, each of which builds — the mechanical change separated from the
substantive one, so that neither diff hides in the other.

## What stayed

Identity in full breadth. The page, minus its project and its labels. The
history, as a mechanism whose rows name their subject. `Refusal` and
`RefusalCode`. Idempotency. The version header and the skew refusal
([0011](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0011-the-api-carries-no-version-and-migrations-only-run-forward.md)).
Full-text search through Postgres. The four layers and the test that keeps
their dependencies pointing inward
([0002](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0002-the-backend-is-four-layers-not-one-project.md)).
The Go CLI as a client of the public API and nothing else
([0003](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0003-the-cli-is-go-not-a-second-dotnet-binary.md)).
The shell
([0006](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0006-the-web-application-is-a-shell-before-it-is-a-screen.md)),
`deploy/`, and the workflows.

## What went

Project, issue, epic, release, label, claim, `next`, question, comment,
sub-issue, the triage and review switches, and the wake channel —
`LISTEN`/`NOTIFY` and everything that waited on it. That is planaffe's domain,
and hostingaffe has nothing to wait for.

The project dimension was the invasive one. In planaffe almost everything
hangs on a project, and the CLI reads a file so that a command can leave it
out. VISION 9 knows no projects: one instance holds one team's infrastructure,
every user sees all of it, and there is no role beyond the administrator. So
the project fell out of the domain, the API, the shell and the CLI, and the
page became instance-wide.

The migration chain went with it. Twelve migrations describing planaffe's
schema plus two drop migrations would have been archaeology with nothing to
show for it; nothing had been released, so the chain was founded again on one
migration that creates what is actually here. That was the one occasion there
will be — from here migrations only run forward.

## Consequences

**It is not extracted.** No shared package, no submodule, no template
repository. A bug in a part both products carry is fixed twice, and that is
the price. A shared package would cost a release process, a version to keep in
step, and a dependency between two products that are deliberately separate —
paid on every change, to save a second fix that happens rarely. Extraction is
reconsidered when a third product needs the same parts, and not before.

**The two copies drift, and that is allowed.** hostingaffe's shell already
looks different from planaffe's, and its identity model is planaffe's minus
the project dimension. Neither has to be kept in step with the other, and a
change here is never blocked on what it would mean over there.

**planaffe's ADRs are referenced, never copied.** They decided the stack, they
still hold, and restating them here would create two texts that can disagree.
`docs/adr/README.md` lists the ones adopted. One that stops fitting this
product is superseded by an ADR here, which says which and why.

**The vision's guard rails are load-bearing.** VISION 12 already says "copied,
not shared"; this ADR is the record that it was carried out, and what it cost.