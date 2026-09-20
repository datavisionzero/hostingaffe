# A Provider Is a Record, and a VM Inherits It

The original closed model has three relationships and keeps
`machine.provider` as free text. That cannot give a provider a description or
answer which machines it supplies without treating spelling as identity. We
deliberately add a keyed Provider entity and a fourth relationship: an optional
assignment from a non-VM machine to a provider. This replaces the free-text
field; it does not open a configurable relationship system.

A VM gets its effective provider through its host machine, recursively if the
host is itself a VM. It cannot carry a separate assignment. Local machines are
unassigned unless an operator explicitly records an external provider. Changing
a host changes the provider the VM displays. Deleting and restoring a machine
retain its assignment, so deletion of an assigned provider is refused while
any machine, even a deleted one, still references it. Provider deletion is a
soft delete with a reserved key, and its edits and lifecycle are history acts.

The forward-only migration creates provider records from each distinct,
nonblank legacy value. It saves the original text exactly on each machine as a
read-only `legacy_provider` field. Provider names use the source text, while
keys follow the normal lowercase key grammar. A deterministic digest suffix
disambiguates values that normalize to the same key or need truncation. Thus
mixed case, punctuation and invalid key characters lose no information.
Whitespace-only values have no assignment but remain in `legacy_provider`.
Non-VM machines receive the new association. VMs inherit the host's provider;
an old VM value that disagrees remains visible in `legacy_provider`, including
on API, CLI and web reads. The field is historical evidence, not a second
provider assignment. Installations and existing history are untouched.

The two graphical maps read these records without inventing edges. The hosting
map at `/hosting-map` shows only provider-to-machine edges, machine address
fields and a group for unassigned machines. A machine's map at
`/machines/{key}/installations-map` shows only machine-to-installation edges.
It starts with platform installations collapsed; expanding them or using the
full adjacent list reaches every installation. Deployments order entries by
their latest time, newest first, with undeployed entries after them. Web
application nodes show every distinct domain derived from their recorded URLs.
Neither map edits relationships. Ordinary detail and list links remain usable.

This narrows [ADR 0018](0018-the-front-page-is-a-tile-per-machine.md): its
no-graph rule continues to apply to the front-page tiles and monitoring. It
does not rule out navigation diagrams on separate routes. The record still
stores text and fields only; diagrams are rendered views of them.
