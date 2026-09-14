import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { MachineView } from "./MachineView";

const identity = { id: aUser.id, kind: "user", name: aUser.name };

const machine = {
  key: "ex44", name: "ex44", hostname: "ex44", kind: "dedicated", host: null,
  provider: "example-hoster", plan: "EX44", location: "fsn1", os: "Ubuntu 26.04 LTS",
  arch: "amd64", cpu: null, memory: null, disk: null, ipv4: null, ipv6: null,
  private_ip: null, ssh: null, ports: [], status: "active", measured_at: null, last_seen: null,
  reboot_required: null,
  drift: [],
  description: "", created_by: identity, updated_by: identity,
  created_at: "2026-09-02T10:00:00Z", updated_at: "2026-09-02T10:00:00Z",
};

const report = {
  machine: "ex44",
  number: 7,
  received_at: new Date(Date.now() - 12 * 60 * 1000).toISOString(),
  collected_at: new Date(Date.now() - 12 * 60 * 1000).toISOString(),
  agent: "0.4.0",
  host: {
    hostname: "ex44", os: "Ubuntu 26.04 LTS", kernel: "6.14.0-27-generic", arch: "x86_64",
    uptime_seconds: 1893244, load1: 0.14, load5: 0.2, load15: 0.18,
  },
  memory: {
    total_bytes: 67430400000, used_bytes: 19204000000, available_bytes: 46900000000,
    swap_total_bytes: 0, swap_used_bytes: 0,
  },
  disks: [{ mount: "/", device: "/dev/nvme0n1p2", size_bytes: 502000000000, used_bytes: 301000000000, percent: 60 }],
  containers: [
    {
      name: "logaffe", image: "ghcr.io/datavisionzero/logaffe:1.4.0", state: "running",
      status: "Up 3 days", health: "healthy", restarts: 0,
      started_at: "2026-09-10T09:12:00Z", ports: ["127.0.0.1:18502->8080/tcp"],
    },
    {
      name: "caddy", image: "caddy:2.10", state: "exited", status: "Exited (0)",
      health: null, restarts: 3, started_at: null, ports: [],
    },
  ],
  listening: [
    { port: 22, protocol: "tcp", binding: "public" },
    { port: 18502, protocol: "tcp", binding: "loopback" },
  ],
  updates: { reboot_required: true },
  missing: [],
  drift: [],
};

function view(routes: Parameters<typeof installInstance>[0] = {}) {
  const instance = installInstance({
    "GET /api/machines/ex44": machine,
    "GET /api/machines/ex44/history": [],
    "GET /api/machines/ex44/files": [],
    "GET /api/installations": [],
    "GET /api/pages": [],
    "GET /api/machines/ex44/reports": { total: 0, reports: [] },
    "GET /api/machines/ex44/reports/latest": { status: 404, body: { detail: "ex44 has never reported." } },
    "GET /api/machines/ex44/token": { present: false, prefix: null, issued_by: null, issued_at: null, last_used_at: null, revoked_by: null, revoked_at: null },
    ...routes,
  });

  renderAt("/machines/ex44", <Routes><Route path="/machines/:key" element={<MachineView />} /></Routes>);

  return instance;
}

afterEach(() => vi.unstubAllGlobals());

describe("what a machine says about itself (VISION 7, ADR 0015)", () => {
  // The ordinary state of a machine on which no cron has been set up: a
  // sentence saying what would make it report, not an empty box and not a
  // red line.
  it("says what a machine that never reported would need", async () => {
    view();

    expect(await screen.findByText(/This machine does not report/)).toBeInTheDocument();
    expect(await screen.findByText("This machine has never reported.")).toBeInTheDocument();
  });

  it("shows the last report: the host, the disks, and the containers with their tags", async () => {
    view({ "GET /api/machines/ex44/reports/latest": report });

    expect(await screen.findByText("6.14.0-27-generic")).toBeInTheDocument();
    expect(screen.getByText("21d 21h")).toBeInTheDocument();
    expect(screen.getByText("0.14 0.20 0.18")).toBeInTheDocument();
    expect(screen.getByText("19.2GB of 67.4GB used")).toBeInTheDocument();

    // The image carries its tag: that is what the record is compared against.
    expect(screen.getByText("ghcr.io/datavisionzero/logaffe:1.4.0")).toBeInTheDocument();
    expect(screen.getByText("1 of 2 containers running")).toBeInTheDocument();

    // A container that is not running is recognisable without the table
    // breaking out in alarm colours.
    expect(screen.getByText("exited")).toBeInTheDocument();
    expect(screen.getByText("301.0GB of 502.0GB")).toBeInTheDocument();
  });

  // What listens, and nothing about what is listening: the collector needs no
  // root, and a process name is what would have cost it one (HOST-19).
  it("says what listens, how far it is bound, and nothing about the process", async () => {
    renderWith({ "GET /api/machines/ex44/reports/latest": report });

    expect(await screen.findByText("2 listening ports")).toBeInTheDocument();
    expect(screen.getByText("22/tcp")).toBeInTheDocument();
    expect(screen.getByText("reachable from off this machine")).toBeInTheDocument();
    expect(screen.getByText("18502/tcp")).toBeInTheDocument();
    expect(screen.getByText("loopback only")).toBeInTheDocument();

    // There is no column for it and no room for one.
    expect(screen.queryByText(/sshd/)).toBeNull();
    expect(screen.queryByText(/docker-proxy/)).toBeNull();
  });

  it("says when the machine is waiting for a restart, and says nothing when it is not", async () => {
    renderWith({ "GET /api/machines/ex44/reports/latest": report });
    expect(await screen.findByText("This machine is waiting for a restart.")).toBeInTheDocument();
  });

  it("says nothing about a restart the collector could not determine", async () => {
    renderWith({
      "GET /api/machines/ex44/reports/latest": {
        ...report,
        updates: null,
        missing: [{ section: "updates", reason: "this distribution has no reboot-required marker" }],
      },
    });

    expect(await screen.findByText(/not determined/)).toBeInTheDocument();
    expect(screen.queryByText("This machine is waiting for a restart.")).toBeNull();
  });

  it("says which section the collector could not determine, and why", async () => {
    view({
      "GET /api/machines/ex44/reports/latest": {
        ...report,
        containers: null,
        missing: [{ section: "containers", reason: "docker is not installed" }],
      },
    });

    expect(await screen.findByText(/not determined \(docker is not installed\)/)).toBeInTheDocument();
  });

  // VISION 5 leaves graphs out, and that is meant literally: no chart, not even
  // a small one.
  it("draws no chart", async () => {
    const { container } = renderWith({ "GET /api/machines/ex44/reports/latest": report });

    expect(await screen.findByText("6.14.0-27-generic")).toBeInTheDocument();
    expect(container.querySelectorAll("svg canvas").length).toBe(0);
    expect(container.querySelector("canvas")).toBeNull();
  });

  it("lists the series and opens one of them whole", async () => {
    view({
      "GET /api/machines/ex44/reports": {
        total: 2,
        reports: [
          { number: 7, received_at: report.received_at, collected_at: report.collected_at, containers_running: 1, containers_total: 2, disk_percent: 60, load1: 0.14, reboot_required: true },
          { number: 6, received_at: "2026-09-13T07:45:00Z", collected_at: "2026-09-13T07:45:00Z", containers_running: 2, containers_total: 2, disk_percent: 59, load1: 0.2, reboot_required: false },
        ],
      },
      "GET /api/machines/ex44/reports/6": { ...report, number: 6 },
    });

    const series = await screen.findByRole("heading", { name: "Reports" });
    const section = series.closest("section")!;
    expect(await within(section).findByText("1/2")).toBeInTheDocument();
    expect(within(section).getByText("59%")).toBeInTheDocument();

    await userEvent.click(within(section).getByRole("button", { name: /59%/ }));
    expect(await screen.findByRole("dialog")).toHaveTextContent("Report 6");
  });
});

describe("drift: what the record and the machine disagree about (ADR 0015)", () => {
  const drift = [
    {
      kind: "version", subject: "logaffe-prod", field: "version",
      record: "1.4.0", record_at: "2026-09-08T19:12:00Z",
      reported: "1.3.2", reported_at: report.received_at,
    },
  ];

  it("names both sides with their ages, and gives no verdict", async () => {
    view({ "GET /api/machines/ex44": { ...machine, drift } });

    expect(await screen.findByRole("heading", { name: "Drift" })).toBeInTheDocument();
    expect(screen.getByText("1.4.0")).toBeInTheDocument();
    expect(screen.getByText("1.3.2")).toBeInTheDocument();

    // One click from the record somebody would correct, and no button that
    // corrects it for them: that would be discovery through the back door.
    expect(screen.getByRole("link", { name: "logaffe-prod" }))
      .toHaveAttribute("href", "/installations/logaffe-prod");
    expect(screen.queryByRole("button", { name: /reconcile|apply|sync/i })).not.toBeInTheDocument();
  });

  it("says nothing at all where the two sides agree", async () => {
    view();

    expect(await screen.findByText(/This machine does not report/)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Drift" })).not.toBeInTheDocument();
  });
});

describe("the key a machine reports under (ADR 0016)", () => {
  it("says there is none, and issues one that is shown exactly once", async () => {
    view({
      "POST /api/machines/ex44/token": {
        status: 201,
        body: { machine: "ex44", prefix: "ha_abcdef", secret: "ha_the-secret-of-this-machine", issued_at: "2026-09-13T08:00:00Z" },
      },
    });

    expect(await screen.findByText(/This machine has no token/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Issue" }));
    const confirm = await screen.findByRole("dialog");
    await userEvent.click(within(confirm).getByRole("button", { name: "Issue" }));

    const shown = await screen.findByText("ha_the-secret-of-this-machine");
    expect(shown).toBeInTheDocument();
    // The dialog says plainly that it does not come back, and hands over the
    // line the host needs rather than leaving somebody to assemble it.
    expect(screen.getByText(/shown once and does not come back/)).toBeInTheDocument();
    expect(screen.getByText(/ha report send --quiet/)).toBeInTheDocument();
  });

  it("shows a token it has, when it was issued and when it was last used", async () => {
    view({
      "GET /api/machines/ex44/token": {
        present: true, prefix: "ha_abcdef",
        issued_by: identity, issued_at: "2026-09-13T08:00:00Z",
        last_used_at: new Date(Date.now() - 12 * 60 * 1000).toISOString(),
        revoked_by: null, revoked_at: null,
      },
    });

    expect(await screen.findByText("ha_abcdef…")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Rotate" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Revoke" })).toBeInTheDocument();
  });

  it("says what became of a token that was taken back", async () => {
    view({
      "GET /api/machines/ex44/token": {
        present: false, prefix: "ha_abcdef",
        issued_by: identity, issued_at: "2026-09-13T08:00:00Z", last_used_at: null,
        revoked_by: identity, revoked_at: "2026-09-13T09:00:00Z",
      },
    });

    expect(await screen.findByText(/was revoked/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Issue" })).toBeInTheDocument();
  });
});

function renderWith(routes: Parameters<typeof installInstance>[0]) {
  installInstance({
    "GET /api/machines/ex44": machine,
    "GET /api/machines/ex44/history": [],
    "GET /api/machines/ex44/files": [],
    "GET /api/installations": [],
    "GET /api/pages": [],
    "GET /api/machines/ex44/reports": { total: 0, reports: [] },
    "GET /api/machines/ex44/token": { present: false, prefix: null, issued_by: null, issued_at: null, last_used_at: null, revoked_by: null, revoked_at: null },
    ...routes,
  });

  return renderAt("/machines/ex44", <Routes><Route path="/machines/:key" element={<MachineView />} /></Routes>);
}
