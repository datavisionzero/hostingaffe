# Architecture Decisions

This directory contains decisions that are difficult to reverse, would be
surprising without context, and resulted from a genuine trade-off. Product scope
and promises belong in the [product vision](../../Vision.md) instead, which also
carries the roster of the technical direction — an entry there names what was
chosen, an ADR here explains why the obvious alternative was not.

The stack itself was not decided here. It is planaffe's, adopted rather than
re-decided, and the ADRs that chose it live in
[`datavisionzero/planaffe`](https://github.com/datavisionzero/planaffe/tree/main/docs/adr).
They are referenced, never copied. What this directory holds is what
hostingaffe decides for itself.

## Naming

ADRs are numbered sequentially as `NNNN-short-slug.md`. The next number follows
the highest existing number.

## Short form

```md
# Short decision title

One to three sentences describe the context, decision, and rationale.
```

Status, considered options, and consequences are included only when they add
material value to understanding the decision.

## Decisions

- [0001 – The foundation is a copy of planaffe](./0001-the-foundation-is-a-copy-of-planaffe.md)
- [0002 – The API lives under /api](./0002-the-api-lives-under-api.md)
- [0003 – A page's slug is one segment, not a path](./0003-a-pages-slug-is-one-segment-not-a-path.md)

## Adopted from planaffe

Referenced where they apply, and not restated:

| | |
|---|---|
| [0002](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0002-the-backend-is-four-layers-not-one-project.md) | the backend is four layers, not one project |
| [0003](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0003-the-cli-is-go-not-a-second-dotnet-binary.md) | the CLI is Go, not a second .NET binary |
| [0004](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0004-the-frontend-is-react-not-blazor.md) | the frontend is React, not Blazor |
| [0005](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0005-the-contract-is-checked-in-and-both-clients-are-generated-from-it.md) | the contract is checked in and both clients are generated from it |
| [0006](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0006-the-web-application-is-a-shell-before-it-is-a-screen.md) | the web application is a shell before it is a screen |
| [0007](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0007-markdown-is-rendered-in-the-browser-and-never-as-html.md) | Markdown is rendered in the browser and never as HTML |
| [0011](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0011-the-api-carries-no-version-and-migrations-only-run-forward.md) | the API carries no version and migrations only run forward |
| [0013](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0013-deleting-is-a-soft-delete-with-a-floor-and-identities-are-never-deleted.md) | deleting is a soft delete with a floor, and identities are never deleted |
| [0015](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0015-a-token-is-an-agent-or-a-users-key-and-an-agent-is-never-an-administrator.md) | a token is an agent or a user's key, and an agent is never an administrator |
| [0017](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0017-the-web-application-is-drawn-by-tailwind-and-base-ui-components-the-repository-owns.md) | the web application is drawn by Tailwind and Base UI components the repository owns |
| [0018](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0018-transactional-email-is-an-optional-instance-capability.md) | transactional email is an optional instance capability |
| [0021](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0021-a-pages-address-is-its-slug-not-a-key.md) | a page's address is its slug, not a key |

An adopted ADR that stops fitting this product is not edited over there. It is
superseded by an ADR here, which says which one and why.
