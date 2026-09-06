# Security Policy

hostingaffe is meant to be reachable over the network by the people and the agents
that use it: every request but `GET /version` carries a user token or an agent
token, and what a token may do is decided by the server from the row it finds,
never by the client. Security is therefore part of the product rather than
something the operator is expected to solve with a VPN in front of it.

## Reporting a vulnerability

Please report security issues privately through GitHub's private vulnerability
reporting: open the **Security** tab of `datavisionzero/hostingaffe` and choose
**Report a vulnerability**.

Do not open a public issue for a suspected vulnerability, and do not disclose it
publicly before a fix is available.

Please include:

- what the issue is and which surface it affects (the HTTP API, the bootstrap,
  the CLI, the web application, the container image),
- the steps needed to reproduce it,
- the version or commit you tested against,
- the impact you believe it has.

## What to expect

This is a small project maintained by a single person. Reports are acknowledged
as soon as they are seen, and fixes are prioritized over other work. There is no
bug bounty.

## Supported versions

hostingaffe is pre-release. Only the current `main` branch receives security fixes;
there are no maintained release branches yet. This section will be updated once
versioned releases exist.

## Scope

In scope:

- token authentication and the bootstrap of the first administrator,
- the line between a user and an agent — an agent never administers the
  instance, never creates a token, never takes over a user's claim,
- anything that lets one identity act as another, or read what the permission
  model says it may not,
- the claim: anything that lets two identities hold one issue at once,
- the default configuration of the published container image and Compose setup,
  once they exist.

Out of scope:

- weaknesses that require the attacker to already hold the database connection
  string or host access to the machine hostingaffe runs on,
- the content of issues, comments and questions. hostingaffe stores Markdown as
  delivered and renders none of it on the server; what an agent does with text
  it reads from a ticket is the agent's harness's business,
- denial of service by an identity that legitimately holds a token. Tokens are
  issued by the operator to their own people and agents, which are trusted by
  design.

## No warranty

hostingaffe is provided under the MIT License, without warranty of any kind. See
[LICENSE](LICENSE).
