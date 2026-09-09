# Installing an instance

Written for an agent to execute, and readable by the person watching it. From
nothing to an instance you can sign in to, on one host, in one command and a
few answers.

## What the host needs

Docker with the Compose plugin, and nothing else. No .NET, no Node, no Go, no
Postgres: the image carries the API and the built web application, and Compose
brings the database up beside it (VISION 12, 16).

## The four values

```sh
git clone https://github.com/datavisionzero/hostingaffe.git
cd hostingaffe
cp deploy/.env.example deploy/.env
```

`deploy/.env` is ignored by git and never leaves the host. Four values have to
be set; everything else in the file has a default and can stay commented out.

| | |
|---|---|
| `POSTGRES_PASSWORD` | any long random string; the two services share it |
| `HOSTINGAFFE_BOOTSTRAP_ADMIN` | the name of the first administrator |
| `HOSTINGAFFE_BOOTSTRAP_EMAIL` | their email address |
| `HOSTINGAFFE_BOOTSTRAP_TOKEN` | their first user token, at least 32 characters |

The bootstrap token is what the CLI carries as `HOSTINGAFFE_TOKEN`. It is read
on the first start only, when the instance has no identity yet, and ignored on
every start after that — so it can be rotated like any other token
afterwards, and the variable left where it is.

Generate the two secrets rather than typing them:

```sh
openssl rand -base64 33   # POSTGRES_PASSWORD
openssl rand -base64 33   # HOSTINGAFFE_BOOTSTRAP_TOKEN
```

A variable that has to be set and is not stops Compose before anything runs,
and the message names it.

## Start it

```sh
docker compose -f deploy/docker-compose.yml up -d
```

Nothing has to be set for this to find an image: until the first release, the
Compose file names `:main`, the build of the trunk. `HOSTINGAFFE_IMAGE` is
where an installation says otherwise — a version to stand still on, or an image
it built itself:

```sh
docker build -f deploy/Dockerfile -t hostingaffe:local .   # HOSTINGAFFE_IMAGE=hostingaffe:local
```

The instance migrates its own schema, creates the first administrator, and
serves on `8080`. It is up when this answers:

```sh
curl -s localhost:8080/api/version
```

## Sign in

The web application is on the same port. Open `http://<host>:8080/`. The first
sign-in is the one that uses the bootstrap token: it exchanges the token for a
session and sets the administrator's password. Every sign-in after that is the
**email address** and that password — the name is a handle, not a login.

Over plain HTTP the session cookie is set without the `secure` flag, because a
browser stores no other kind there — which also means the session travels in
the clear, readable by anything on the way. That is a trial, not an
installation: put TLS in front of it before the second person signs in, and the
instance sets the strict cookie by itself the moment the request reaches it as
HTTPS.

From the console instead:

```sh
export HOSTINGAFFE_URL=http://<host>:8080
export HOSTINGAFFE_TOKEN=<the bootstrap token>
ha me
```

`ha` is one static binary; the release page carries one per platform
(`docs/cli.md`).

## Behind a reverse proxy

The instance speaks plain HTTP and terminates no TLS. Put a proxy in front of
it, publish `8080` to `127.0.0.1` only, and tell the instance which address the
proxy has so that a caller's address and scheme are the caller's:

```sh
HOSTINGAFFE_PORT=127.0.0.1:8080
HOSTINGAFFE_TRUSTED_PROXY=all
```

`all` is right where the proxy is the only thing that can reach the
instance — which is what binding to `127.0.0.1` makes true. An address or a
CIDR network is the answer where it is not.

## Email, if you want invitations

Transactional email is optional (VISION 9). Without it the instance runs and
the administrator is in, but inviting a user, resending an invitation,
password recovery and changing an address all refuse with
`smtp-not-configured`. To turn it on, set the SMTP block in `deploy/.env` and
`HOSTINGAFFE_PUBLIC_URL` to the address the links have to lead to, then restart
and let the instance send itself a test mail from the administration screen.

Two of those values are refused outside a development environment, because an
invitation link is a secret in an email: `HOSTINGAFFE_PUBLIC_URL` has to be
`https`, and `HOSTINGAFFE_SMTP_SECURITY` may not be `none`. A wrong one stops
the start and the log says which.

## What to do next

- Take the first backup and know how (`docs/operations.md`).
- Invite the rest of the team.
- Give each agent a token of its own: `ha agent create --name <name>`. Every
  agent its own, so the history says which one acted.
- Copy the block from [`agents-md.md`](./agents-md.md) into the `AGENTS.md` of
  the repositories whose hosts this instance records, so that an agent working
  there knows where the record is and how to reach it.
