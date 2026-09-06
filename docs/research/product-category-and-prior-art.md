# Product category and prior art

**Question:** What is the category of a tool that records machines, the
software installed on them and the deployment history — and what do the
existing tools in that category model, so that we can borrow the right ideas
and refuse the rest?

**Date:** 2026-09-05. Gathered by research agents from primary sources
(official documentation and repositories); community sentiment is
directional, not quoted. Conclusions in [`Vision.md`](../../Vision.md).

## 1. What the category is called

Four overlapping terms, none an exact fit:

- **CMDB** (configuration management database): the ITIL term for a store of
  configuration items *and their relationships*. Its defining feature is impact
  analysis — "which services are affected if this machine fails". The closest
  match for our data model ([ServiceNow](https://www.servicenow.com/products/it-operations-management/what-is-cmdb.html),
  [Wikipedia](https://en.wikipedia.org/wiki/Configuration_management_database)).
- **ITAM** (IT asset management): lifecycle, procurement, warranty,
  depreciation. Snipe-IT is ITAM-first. A weak fit; we have almost no
  financial or lifecycle concern.
- **Software inventory / SAM**: which software is installed or licensed.
  Narrower than what we need; no machines, no relationships.
- **IT documentation**: the MSP-community term (i-doit, Hudu, ITFlow) for
  structured records plus runbooks, passwords and notes, written for humans to
  read rather than for ITIL compliance. The closest match for our *experience*.
- **Software catalog / internal developer portal** (Backstage, Port, OpsLevel,
  Cortex): typed entities, declared relationships, docs attached — the closest
  match for our *architecture*, for software components instead of machines.

**Best description of hostingaffe:** a lightweight, developer-centric CMDB with
an IT-documentation experience, written by agents through a CLI.

## 2. The tools, and why each is not it

| Tool | Models | Why not for us |
| --- | --- | --- |
| [NetBox](https://netboxlabs.com/docs/netbox/) (Apache-2.0) | DCIM (racks, devices, interfaces), IPAM, VMs, circuits; `Service` objects on devices/VMs; custom fields, tags; REST + GraphQL; [change log](https://netboxlabs.com/docs/netbox/features/change-logging/) and [journal](https://netboxlabs.com/docs/netbox/features/journaling/); an [MCP server](https://github.com/netboxlabs/netbox-mcp-server) | Network-topology-centric; no runbooks; a VPS renter has no racks, cables or VLANs. Nautobot is the same, heavier. |
| [i-doit](https://i-doit.org/en/) (AGPL) | Documentation-first, flexible object model, relationships | PHP/MariaDB; the open edition's customisation is limited without Pro; built for IT departments. |
| [GLPI](https://github.com/glpi-project/glpi) (GPL) | ITSM suite: assets, DCIM, helpdesk, licences, contracts, KB | The CMDB is one module in a helpdesk platform; custom fields via [plugin](https://github.com/pluginsGLPI/fields). |
| [iTop](https://github.com/Combodo/iTop) (AGPL) | CMDB + ITSM; relationships as first-class objects with impact analysis | Bundled ticketing and SLAs; heavy. Its relationship model is worth knowing. |
| [Snipe-IT](https://snipe-it.readme.io/docs/custom-fields) (AGPL) | Owned hardware: checkout, warranty, depreciation; custom fieldsets | No services, no versions, no relationships. |
| Ralph | DCIM/CMDB for data centres | Discontinued (Allegro, January 2024). |
| RackTables, [openDCIM](https://opendcim.org/) | Racks, cabinets, cabling | Data-centre hardware; read-only or no API. |
| [Homebox](https://github.com/sysadminsmedia/homebox) (Go/SQLite) | Household items, locations, warranties | Not servers — but the footprint (single binary, tiny) is what people praise. |
| [ITFlow](https://github.com/itflow-org/itflow) (GPL), [Hudu](https://www.hudu.com/) (commercial) | MSP documentation: clients, assets, passwords, docs, domains, billing | Modelled around *clients*; no version history; Hudu is proprietary. Hudu's Markdown docs are the closest UX analogue. |
| Device42, Lansweeper, ServiceNow CMDB | Enterprise, discovery-driven | Discovery infrastructure, enterprise pricing. ServiceNow's relationship taxonomy (runs on / depends on / contains / uses) is a useful reference. |
| [Backstage](https://backstage.io/docs/features/software-catalog/system-model/) | Component, API, Resource, System, Domain in `catalog-info.yaml`; `dependsOn`, `partOf`, `ownedBy`; [TechDocs](https://backstage.io/docs/features/techdocs/) | Node backend, Postgres, background workers, a docs build pipeline; assumes many repositories and a platform team. Roadie's ["real to-do list"](https://roadie.io/blog/self-hosting-backstage-the-real-to-do-list/) describes the cost. |
| [Port](https://www.getport.io/), OpsLevel, [Cortex](https://www.cortex.io/) | Blueprints/entities/relations; scorecards and rubrics | SaaS; scorecards compare teams, and a team of one has nobody to compare against. |

Community pattern (r/homelab, r/selfhosted, HN — directional): small operators
prefer small, low-maintenance, API-friendly tools; NetBox is recommended for
homelabs *despite* being network-centric, because of its API; GLPI, iTop and
ServiceNow are consistently called overkill. Passwords, contracts and discovery
are secondary asks even in MSP communities.

## 3. Deployment tracking elsewhere

Octopus Deploy's dashboard (project × environment → latest release), Argo CD's
`status.sync.revision` plus a bounded `status.history`, Google's Four Keys
schema (`events_raw(source, event_type, id, metadata, time_created)`, joined
on a commit SHA), Grafana annotations (`time, text, tags`), Vercel and Render
deployment records (branch, SHA, author, status), Kubernetes rollout history,
and the image-update watchers (Watchtower, [Diun](https://crazymax.dev/diun/),
[WUD](https://github.com/getwud/wud)) all converge on one shape:

**a content-addressable reference (git SHA, image digest, revision) is the
source of truth, and "what runs where" is the latest event per (entity,
environment).**

Minimal event model synthesised from them: entity, environment, from-version,
to-version, reference, time, actor, note. We keep that and drop the status
column: a row is recorded when a version actually ran.

## 4. Structured Markdown elsewhere

Front matter (Jekyll, Hugo, Docusaurus) is structured but unvalidated.
Obsidian Properties with Dataview or [Bases](https://obsidian.md/help/bases)
turn front matter into queryable tables. Notion databases are the cleanest
precedent for "fixed typed properties above a free-form body". BookStack's
name/value tags are lightweight structure on a page tree. Validation of front
matter against a per-type schema exists in
[Astro content collections](https://docs.astro.build/en/guides/content-collections/)
(Zod at build time) and in linters such as zod-matter and
remark-lint-frontmatter-schema; document-schema.org validates section
structure as well, aimed at agent-written docs.

What we take from this: typed fields above a Markdown body per entity, a
template on creation, tables across entities generated from the fields — and
we do it in a database with a CLI rather than in files, because the writer
must be checked at write time and the history must be a record.

## 5. Agent-oriented infrastructure documentation

Sparse and early. NetBox has a read-only official MCP server and a paid
platform preview; Backstage has an early MCP actions plugin and an open RFC on
modelling MCP servers as entities; homelab MCP servers are hobby projects;
agent-executable runbooks are blog-post-stage. No tool was found whose premise
is "an agent documents your infrastructure through a CLI". `llms.txt` is a
relevant convention for an agent-facing export.

## 6. What we borrow

- NetBox's separation of the automatic change log from human-written journal
  notes → our history plus the `--note` on every write.
- NetBox's "a service belongs to either a device or a VM" → an installation belongs
  to a machine, and a machine may be a VM on another machine.
- Argo CD / Four Keys → deployment as an event with a reference; current
  version derived from the latest event.
- Backstage → docs attached to entities rather than stored inside them;
  declared relationships, but only the two we need.
- Notion → typed fields above a free Markdown body.
- Homebox → the footprint people actually want.

## 7. What we refuse

Custom fields and configurable types (Snipe-IT, GLPI, i-doit, Port);
discovery (Device42, Lansweeper, NetBox Diode); racks, cables, IPAM (NetBox,
RackTables, openDCIM); helpdesk, contracts, procurement (GLPI, iTop, ITFlow);
scorecards (OpsLevel, Cortex); a docs build pipeline (Backstage TechDocs);
passwords in the documentation tool (Hudu, ITFlow) — vaultaffe holds those.
