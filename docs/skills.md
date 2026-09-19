# Agent skills

Two optional skills for the agent that works on your hosts. Each is a folder
under [`skills/`](../skills) with a `SKILL.md` in it, versioned here with the
CLI, and each is a procedure written in ordinary Markdown — they add no command
to `ha` and no endpoint to the API.

| Skill | What it carries through |
|---|---|
| [`hostingaffe-update-installation`](../skills/hostingaffe-update-installation/SKILL.md) | A software named by the user, resolved to the installations that run it, brought to a new version through the record, and recorded as a deployment |
| [`hostingaffe-new-installation`](../skills/hostingaffe-new-installation/SKILL.md) | Something new on a machine: the software, the key, the installation, its files, what it depends on, its secrets by name, its first deployment and its runbook |

They are installed into the agent working on **your** hosts, not into the
development of hostingaffe. They need `ha` on the path and `HOSTINGAFFE_URL`
and `HOSTINGAFFE_TOKEN` in the environment, and they provision neither.

## They sit on top of the block, and do not repeat it

[`agents-md.md`](./agents-md.md) is the paragraph every agent has in context:
how to read a host, how to change a file, how to record what it did. A skill is
loaded when its name fits what the user asked for, and holds a **procedure** —
the order of the steps, the branch where a question has to be asked, the write
that must not be skipped at the end.

The division matters when either changes. The block stays the baseline, the
skills say what to do with it, and neither contradicts the other. Both name
verbs of `ha`, so both are part of the CLI's surface in practice: a verb that is
renamed is renamed here too, or the block and the skills start lying on the same
day.

## What they deliberately do not contain

**How a given software is installed or updated.** That is in the record — in the
installation's description and in its runbook pages — and it is different on
every host. A skill that carried its own instructions would be wrong about half
of them and stale on the rest, and it would make hostingaffe into the deployment
engine [`Vision.md`](../Vision.md) says it is not. The skills read the runbook
and follow it; where there is none, they ask the user and offer to write one.

They also contain no instance address, no token, no host of ours and no real
address range. Every example in them is invented, the way every example in this
repository is.

## Install

For Claude Code, Codex, opencode and the other harnesses that read the
[Agent Skills](https://agentskills.io) format:

```sh
npx skills@latest add datavisionzero/hostingaffe -g
```

`-g` installs into the agent's user-level directory, where every repository
sees them. Without it they land in whichever project you are standing in, which
is what you want only if the hosts belong to that one repository.

Copying the folders out of a checkout does the same thing — copy the folder, not
its contents, into `~/.claude/skills/`, `~/.codex/skills/` or the equivalent.
Pin them to the same tag as the `ha` you run when you pin anything, and compare
before overwriting a copy you have edited.

## Use

Name the skill, or say what you want and let the harness match it:

```text
update caddy
take app-1 onto ex44
```

**Updating** asks nothing when the software runs in exactly one place, and asks
which one — or all — when it runs in several. It reads `needed by` before
anything restarts and names what will go down with it. It changes the file in
the record first and syncs it to the machine second, runs what the runbook says,
and ends with `ha deploy`. A run that changed the host and recorded no
deployment is a run that is not finished, and it says so.

**Adding** goes the other way round: the software, the machine, the key that can
never be reused, then the installation, its files, what it depends on, the
secrets it needs by name, the first deployment and a runbook page. It ends by
reading `ha machine context` back, which is the document the next agent will
arrive at.

Neither writes a secret value, neither deletes anything, and neither touches an
installation the user did not name — where something else has to move too, they
say so and leave the decision with the user.
