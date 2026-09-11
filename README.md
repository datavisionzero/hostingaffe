# hostingaffe

A self-hosted record of the machines a solo developer or a small team rents or
owns: what is installed on each, which version has been running since when,
which files each installation runs with, and how all of it changed over time.
Written mostly by AI agents through a CLI, read and corrected by humans in a
small web application.

```sh
ha machine context caddy
```

Hosting it is one `docker compose up` with one Postgres beside it, and one
documented backup.

## The one thing that is different

An agent that is about to work on a host has to know what is on it, and the
usual answer is to read a repository of Markdown into its context and hope the
fields were spelled the same way on every machine. Here that is **one call that
returns one document**: the machine and its fields, its installations with the
version each runs, their ports, directories and file lists, the last
deployments, and the runbooks and decisions that apply — ordered so the part an
agent needs first comes first. File contents are not in it; they are one
`ha files get` away.

```sh
ha machine context caddy                                   # everything before touching the host
ha files get compose.yml --installation logaffe-prod       # and the revision with it
ha deploy logaffe-prod --version 1.4.0 --note "memory limit"
ha search "18502"                                          # a port, an address, a name, a word
ha search /srv/caddy                                       # a path, or a piece of one
```

`ha` needs `HOSTINGAFFE_URL` and `HOSTINGAFFE_TOKEN` and nothing else, writes
data to stdout and errors to stderr, and answers with exit codes that say which
kind of no it was. A person signs in with `ha login` instead — a short code
approved in a browser, and the token lands in the keychain rather than in a
shell profile. Each agent gets a token of its own, so the history says which
one acted. The block to copy into your own repository's `AGENTS.md` is
[`docs/agents-md.md`](docs/agents-md.md).

## What it deliberately is not

No discovery — no agent on the machine, no SSH scanning, no registry polling.
The record is written, not observed. No deployment engine: hostingaffe writes
an installation's files onto the machine when asked and records that a
deployment happened; it does not run Compose or watch whether it worked, and
it does not become Ansible. No monitoring, no metrics, no alerts. No secret
*values* — an installation names the secrets it needs and the file each one
lies in. No IPAM, no racks, no VLANs. No custom fields and no entity designer:
one fixed set of entities, one fixed set of fields, which is what lets a human
and an agent both understand it in five minutes. No multi-tenancy.

That list is the product decision rather than a roadmap of regrets. If you need
things from it, NetBox and Ansible are the better tools and we would rather say
so. [`Vision.md`](Vision.md) argues each one.

## Running one

```sh
git clone https://github.com/datavisionzero/hostingaffe.git
cd hostingaffe
cp deploy/.env.example deploy/.env    # and set the four values it asks for
docker compose -f deploy/docker-compose.yml up -d
```

The instance migrates its own schema, creates the first administrator from
`deploy/.env`, and serves the API and the web application on `8080`. The first
sign-in is the one that uses the bootstrap token, on `/activate`; every one
after that is `/`, with an email address and a password. It speaks plain HTTP
and terminates no TLS — put a reverse proxy in front of it before the second
person signs in.
[`docs/install.md`](docs/install.md) is the whole path from nothing, written
for an agent to execute.

`ha` is one static binary per platform, from the release page:

```sh
curl -fsSLo ha https://github.com/datavisionzero/hostingaffe/releases/latest/download/ha_linux_amd64
chmod +x ha && sudo mv ha /usr/local/bin/ha
```

Upgrading is `docker compose pull` and `up -d`. Migrations only run forward, so
take the `pg_dump` first — that is the way back.

> **Status: the first release is out.** `v0.1.0` — the record (machines,
> software, installations, deployments, files, pages, history) through the API,
> the web application and `ha`. One image for `linux/amd64` and `linux/arm64`
> on GHCR, pullable without an account, and CLI binaries for Linux, macOS and
> Windows. What has not happened yet is the measure the vision actually set:
> our own machines moved out of their Markdown repositories into an instance
> ([`Vision.md` §16](Vision.md#16-how-we-measure-success)). Expect the rough
> edges of a `0.1`.

.NET 10, React, PostgreSQL — the stack is
[planaffe](https://github.com/datavisionzero/planaffe)'s, adopted rather than
re-decided. The CLI is Go, so that it is one static binary with no runtime on a
host that has enough on it already.

| | |
| --- | --- |
| [`Vision.md`](Vision.md) · [`CONTEXT.md`](CONTEXT.md) | what this is, and the words it is named after |
| [`docs/install.md`](docs/install.md) · [`docs/operations.md`](docs/operations.md) | installing one, and running it: variables, backup, restore |
| [`docs/cli.md`](docs/cli.md) · [`docs/api.md`](docs/api.md) | the console, and the HTTP contract under it |
| [`docs/agents-md.md`](docs/agents-md.md) | the block to copy into your own repository |
| [`docs/codebase.md`](docs/codebase.md) · [`docs/storage.md`](docs/storage.md) · [`docs/adr/`](docs/adr/) | the layout, the data model, and the decisions |

MIT. All of it — no `ee/` directory, no feature behind a second license.
