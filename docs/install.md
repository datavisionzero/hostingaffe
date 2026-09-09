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

The bootstrap token is the first user token: it is what the browser exchanges
for a session on the first sign-in, and what the CLI can carry as
`HOSTINGAFFE_TOKEN` before anybody has signed in. It is read
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

Nothing has to be set for this to find an image: the Compose file names
`:latest`, which is the newest stable release and never a prerelease.
`HOSTINGAFFE_IMAGE` is where an installation says otherwise — `:main` to follow
the trunk, a version to stand still on, or an image it built itself:

```sh
docker build -f deploy/Dockerfile -t hostingaffe:local .   # HOSTINGAFFE_IMAGE=hostingaffe:local
```

The instance migrates its own schema, creates the first administrator, and
serves on `8080`. It is up when this answers:

```sh
curl -s localhost:8080/api/version
```

## Sign in

The web application is on the same port. The first sign-in is the one that
uses the bootstrap token, and it has its own address:

```
http://<host>:8080/activate
```

That screen takes the token and a password, exchanges the one for a session and
sets the other as the administrator's. It is the only screen that accepts the
token: `/` is the ordinary sign-in form, which asks for an address and a
password the instance does not have yet, and it does not link here. Type the
address.

Every sign-in after that is `/`, with the **email address** and that password —
the name is a handle, not a login.

The exchange happens once. The token itself stays an ordinary user token
afterwards, which is what `HOSTINGAFFE_TOKEN` holds for the CLI; it is listed
and revoked like any other.

Over plain HTTP the session cookie is set without the `secure` flag, because a
browser stores no other kind there — which also means the session travels in
the clear, readable by anything on the way. That is a trial, not an
installation: put TLS in front of it before the second person signs in, and the
instance sets the strict cookie by itself the moment the request reaches it as
HTTPS.

From the console instead:

```sh
ha login --url http://<host>:8080
```

`ha` prints a short code and the address to enter it at; open that in the
browser you just signed in with, approve, and the token lands in this machine's
keychain (ADR 0005). Nothing goes into a shell profile. `ha status` then says
which instance this is and as whom.

The bootstrap token is the other way in, and the one that works before anybody
has a password — `HOSTINGAFFE_TOKEN=<the bootstrap token> ha me`. It is also how
an agent always holds a token: from the environment, set by whatever started it.

Over plain HTTP `ha` talks to a loopback host and refuses anything else, because
a token over plain HTTP is a token in the network log (ADR 0006). Reaching a
trial instance on another host by its address is `--insecure-http`, said out
loud — or TLS in front of it, which is the answer anyway.

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
