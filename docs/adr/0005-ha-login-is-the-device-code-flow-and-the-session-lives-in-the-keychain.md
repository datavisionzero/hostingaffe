# `ha login` Is the Device-Code Flow, and the Session Lives in the Keychain

A person signs `ha` in by running `ha login`: the CLI begins a login, prints a
short code, and a person opens `/device` in a browser on whatever machine has
one and approves it. What the CLI collects is an ordinary user token, and it
goes into the **operating system's own store** — Keychain on macOS, the Secret
Service over D-Bus on Linux, through `go-keyring`, which shells out to
`security` on the one and speaks D-Bus on the other and therefore needs no cgo.

Which instance and which token a command runs with are answered by two ladders
rather than by two variables. The instance is `--url`, then `HOSTINGAFFE_URL`,
then the instance this machine last signed in to. The token is
`HOSTINGAFFE_TOKEN`, then the token file if one was chosen, then the keychain.
`ha status` prints both answers with the rung each came from.

## What forced it

[Vision §6.1](../../Vision.md#61-cli) said configuration was two environment
variables and nothing else, and for an agent that is still exactly right: a
harness sets `HOSTINGAFFE_TOKEN` and the agent never thinks about it again. It
was wrong for the other half of the audience. A person who wants to use `ha`
had to obtain a token in a browser, copy it out of the one screen that shows it,
and paste it into a shell profile — where it stays, in plain text, on every
machine they ever typed it on, for as long as they keep the file.

That is the credential this product's own subject matter argues hardest about:
a token here reads every machine the team has. It should not be sitting in
`~/.bashrc` because the CLI offered nowhere else to put it.

The device-code flow is also the only sign-in that works where `ha` actually
runs. There is no browser in an SSH session on a rented box, in a CI job, in a
container, or in an agent's sandbox — and an OAuth redirect to `localhost`
needs one on the machine doing the asking. A short code a person carries to
another machine needs nothing on this one.

## The alternatives

**Password sign-in in the terminal.** One command, no second machine. Rejected:
it is interactive, and the CLI's own promise is that it never prompts
([`cli.md`](../cli.md)) — a prompt in an agent's terminal is a command that
hangs. It also teaches people to type the password that guards every machine
into whatever shell they happen to be in, which is the habit phishing depends
on.

**A browser redirect to a loopback port.** What `gh auth login` does on a
desktop. Rejected as the only path for the reason above: it fails in exactly
the places this CLI is meant to be used, and `gh` itself falls back to a code
for them. Having one flow that works everywhere is worth more than a slightly
shorter one that works in half the places.

**A second kind of token — a "session" with its own table and its own
lifetime.** Rejected: a user token already is a person's key to the CLI
(`CONTEXT.md`, Identity), already appears in `ha token list`, and is already
revocable there and in the browser. A second kind would be a second thing to
revoke, a second thing to explain, and a second row for the identity model to
carry.

**Falling back to a plaintext file where there is no keychain.** This is the
decision, and it is the failure case rather than the happy one. Everybody puts
the token in the keychain; the interesting question is what happens on a
headless Linux with no Secret Service. Writing the credential to a file without
saying so — which is what several well-known CLIs do — inverts the product's
own promise on exactly the machines where it matters most. Refusing silently
would be as bad: a CLI that just fails on a headless box is a CLI somebody
works around with a shell alias, and the workaround will be worse than what we
would have offered.

So the refusal is a sentence with two named ways on, both of which the person
has to choose out loud:

- a token in `HOSTINGAFFE_TOKEN`, which is how an agent receives one anyway;
- `ha login --token-file <path>`, written `0600`, its path recorded in the
  configuration. A file others can read is refused on the way back in, with the
  `chmod` that fixes it — a file mode is the only protection a token in a file
  has, and shrugging at `0644` would be the quiet fallback by another route.

## How the flow is put together

`POST /api/device/logins` answers a **device code** the CLI keeps and a **user
code** it prints, with `verification_uri`, `expires_in_seconds` and
`interval_seconds`. The user code is eight consonants as `XXXX-XXXX`: no vowel,
so it is never a word, and no digit, so none of `0/O`, `1/I`, `5/S` or `2/Z`
has a second half to be confused with. The credential is the device code, and
it is 256 bits; the row keeps its hash and never the code.

**The verification addresses are relative to the instance** — `/device` and
`/device?code=XXXX-XXXX`. The instance stands behind a proxy and would have to
be told its own public name to build a whole one; `ha` has the host already,
and joins the two halves before printing, because a path with no host is not
something anybody can open.

A signed-in user approves at `/device`. The screen is a write under the
browser's session like any other, so what the waiting machine collects is that
user's own token — which is why an agent is refused there: an agent
administers no identities (planaffe ADR 0015), and minting a token is
administering one.

`POST /api/device/tokens` answers the token once somebody has approved, and
**which refusal it is until then is the whole protocol**: `device-pending`
means keep polling; `device-denied`, `device-expired` and `not-found` mean
stop. A device code hands over one token and never a second — the poll that
collects it claims the row in the same transaction — so one left behind in a CI
log is worth nothing to whoever finds it.

## Consequences

**A login lives ten minutes.** Long enough to walk to another machine, short
enough that an abandoned code is not lying around for an afternoon. An approval
nobody collected in time is expired rather than approved.

**One keychain entry per instance**, keyed by the address, so two instances on
one machine do not overwrite each other's session — and `ha logout` removes
exactly one of them.

**The configuration file holds no credential.** It holds the instance and, where
one was chosen, the *path* of the token file. It lives at `$HOSTINGAFFE_CONFIG`,
else `$XDG_CONFIG_HOME/hostingaffe/config.json`, else
`~/.config/hostingaffe/config.json`, and is written `0600`.

**The keychain is reached through three functions**, injected into the command
tree, so that a test never touches the machine's own store and CI needs no
Secret Service to run the CLI's tests.

**`ha logout` refuses a token that came from the environment.** That one is the
agent's or CI's and `ha` did not put it there; revoking it from this terminal
would revoke something the person at it may not know they are holding. What it
does revoke is the token the invocation came in under, which is why `GET /me`
now carries the token's id.

**Nothing changes for an agent.** `HOSTINGAFFE_TOKEN` still wins over everything
else, `files sync` on a machine still runs under the token of the SSH session it
is in and writes none to disk (VISION 9), and
[`agents-md.md`](../agents-md.md) still says exactly that.
