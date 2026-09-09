# A Token Never Travels Over Plain HTTP Off Loopback

`ha` refuses `http://` to any host that is not loopback, before the first
request goes out, and says so as a usage error. `https://` is always fine, and
`http://` to `localhost`, `*.localhost`, `127.0.0.1` or `::1` is always fine, so
a development instance works out of the box. The override is explicit and is
never inferred: `--insecure-http`, or `HOSTINGAFFE_INSECURE_HTTP=1`.

## What forced it

Every request `ha` makes carries `Authorization: Bearer <token>`, and a token
here reads every machine the team has and every file those machines run with
(VISION 9). Over plain HTTP that header is in every network log between the
terminal and the instance. The address now also comes from more places than it
used to — a flag, a variable, or the instance a machine signed in to
([ADR 0005](./0005-ha-login-is-the-device-code-flow-and-the-session-lives-in-the-keychain.md))
— and a mistyped scheme that is remembered is a mistake that goes on being made.

## The alternatives

**Refusing plain HTTP outright.** Cleanest sentence, and it breaks the first
five minutes of every contributor: `docker compose up` serves the instance on
`http://localhost:8080` and there is nothing wrong with that. Loopback is the
one place where "somebody could be reading the wire" is not a real claim.

**A warning rather than a refusal.** A warning on stderr is a line an agent
does not read and a person stops seeing on the second day, while the token is
already on the wire by the time it is printed. What makes this worth having is
that it happens *before* the request.

**Deciding by whether the host resolves to a loopback address.** Rejected: it
turns a rule a person can check by reading the URL into one that depends on the
DNS answer of the moment, and a name that resolves to `127.0.0.1` today can
resolve elsewhere tomorrow. The check is on the literal host, and nothing that
merely resolves to one counts.

## Consequences

**It is a usage error, exit 2**, in the same family as an address that is not an
address at all — the mistake is in the environment, not in the instance's
answer.

**The override is worth having.** An instance reached over a private network, a
tunnel, or a proxy that terminates TLS elsewhere is a real arrangement; what the
flag buys is that somebody said so on purpose, in that invocation or in that
environment, instead of it happening quietly.
