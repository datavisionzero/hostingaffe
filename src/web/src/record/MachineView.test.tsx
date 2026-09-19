import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { MachineView } from "./MachineView";

const identity = { id: aUser.id, kind: "user", name: aUser.name };

const machine = {
  key: "web-01",
  name: "web-01",
  hostname: "web-01.example.test",
  kind: "vps",
  host: null,
  provider: "example-hoster",
  plan: "CX22",
  location: "fsn1",
  os: "Debian 13",
  arch: "amd64",
  cpu: "2 vCPU",
  memory: "8 GB",
  disk: "80 GB",
  ipv4: "192.0.2.10",
  ipv6: null,
  private_ip: null,
  ssh: "root@192.0.2.10",
  ports: [{ port: 22, protocol: "tcp", scope: "public" }],
  status: "active",
  measured_at: null,
  last_seen: null,
  drift: [],
  description: "The one that answers the website.",
  created_by: identity,
  updated_by: identity,
  created_at: "2026-09-02T10:00:00Z",
  updated_at: "2026-09-02T10:00:00Z",
};

function view(routes: Parameters<typeof installInstance>[0] = {}) {
  const instance = installInstance({
    "GET /api/machines/web-01": machine,
    "GET /api/machines/web-01/history": [],
    "GET /api/machines/web-01/files": [],
    "GET /api/installations": [],
    "GET /api/pages": [],
    "GET /api/machines/web-01/reports": { total: 0, reports: [] },
    "GET /api/machines/web-01/reports/latest": { status: 404, body: { detail: "web-01 has never reported." } },
    "GET /api/machines/web-01/token": {
      present: false, prefix: null, issued_by: null, issued_at: null,
      last_used_at: null, revoked_by: null, revoked_at: null,
    },
    ...routes,
  });

  renderAt("/machines/web-01", <Routes><Route path="/machines/:key" element={<MachineView />} /></Routes>);

  return instance;
}

afterEach(() => vi.unstubAllGlobals());

describe("a machine (VISION 6.2)", () => {
  it("shows the fields the record holds and leaves out the ones it does not", async () => {
    view();

    expect(await screen.findByText("example-hoster")).toBeInTheDocument();
    expect(screen.getByText("192.0.2.10")).toBeInTheDocument();
    // No IPv6 was recorded, so there is no row for it rather than an empty one.
    expect(screen.queryByText("IPv6")).not.toBeInTheDocument();
  });

  // The machine's own ports: what it listens on and no installation of it
  // answers to. They stand with the addresses, because that is where somebody
  // reads how far the machine is reachable.
  it("shows the ports the machine itself keeps", async () => {
    view();

    expect(await screen.findByText("22/tcp public")).toBeInTheDocument();
  });

  it("says nothing about ports where the machine keeps none", async () => {
    view({ "GET /api/machines/web-01": { ...machine, ports: [] } });

    expect(await screen.findByText("example-hoster")).toBeInTheDocument();
    expect(screen.queryByText("Ports")).not.toBeInTheDocument();
  });

  it("says when the fields were last measured, and says so when they never were", async () => {
    view();

    expect(
      await screen.findByText("These fields have never been measured against the machine itself."),
    ).toBeInTheDocument();
  });

  /**
   * The history section reads its values the way the reading at `/history` does:
   * a moment is a date, and a birth does not print the machine's key back.
   */
  it("writes a moment in the history as a date, and says a birth once", async () => {
    view({
      "GET /api/machines/web-01/history": [
        {
          id: 1, actor: identity, at: "2026-09-02T10:00:00Z",
          field: "created", old_value: null, new_value: "web-01", note: null,
        },
        {
          id: 2, actor: identity, at: "2026-09-19T08:00:00Z",
          field: "measured_at", old_value: null, new_value: "2026-09-19T08:00:00.000000Z", note: null,
        },
      ],
    });

    const created = await screen.findByText("created");
    expect(created.parentElement?.textContent).toBe("created");

    const measured = screen.getByText("measured_at");
    expect(measured.parentElement).toHaveTextContent(new Date("2026-09-19T08:00:00.000000Z").toLocaleString());
    expect(measured.parentElement?.textContent).not.toContain("2026-09-19T08:00:00.000000Z");
  });

  // Every section stands on its own: the fields are the answer somebody came
  // for, and a history that failed must not take them with it.
  it("keeps the screen when one of its sections fails", async () => {
    view({ "GET /api/machines/web-01/history": { status: 500, body: { detail: "The history is unavailable." } } });

    expect(await screen.findByText("example-hoster")).toBeInTheDocument();
    expect(await screen.findByText("The history is unavailable.")).toBeInTheDocument();
  });

  it("lists what is installed on it, and links to each installation", async () => {
    view({
      "GET /api/installations": [{
        key: "caddy-web-01", name: "caddy", machine: "web-01", software: "caddy",
        environment: "production", role: "platform", status: "active",
        backup: "none", monitoring: "external", logging: "central",
        version: "2.8.4", updated_at: "2026-09-02T10:00:00Z",
      }],
    });

    const row = await screen.findByRole("link", { name: /caddy-web-01/ });
    expect(row).toHaveAttribute("href", "/installations/caddy-web-01");
    expect(within(row).getByText("2.8.4")).toBeInTheDocument();
  });

  // The size is the one thing about a file's text a list can say without
  // carrying it, and it is what tells an operator whether a text has grown.
  it("lists the files it carries with their size and revision", async () => {
    view({
      "GET /api/machines/web-01/files": [{
        owner: { kind: "machine", key: "web-01" },
        path: "systemd/logaffe.service", executable: false, size: 412, revision: 3,
        updated_by: identity, updated_at: "2026-09-02T10:00:00Z",
      }],
    });

    const row = await screen.findByRole("link", { name: /systemd\/logaffe.service/ });
    expect(row).toHaveAttribute("href", "/machines/web-01/files/systemd/logaffe.service");
    expect(within(row).getByText("412 B")).toBeInTheDocument();
    expect(within(row).getByText("rev 3")).toBeInTheDocument();
  });

  it("asks only for the installations of this machine", async () => {
    const instance = view();

    await screen.findByText("example-hoster");
    await waitFor(() =>
      expect(instance.calls.some((call) => new URL(call.url).search === "?machine=web-01")).toBe(true));
  });

  // A description is a text an operator and an agent both write, so the write
  // carries the version last read (`docs/api.md`, Guarding a write).
  it("guards a written description with the version it read", async () => {
    const instance = view();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Edit" }));
    await user.click(await screen.findByRole("button", { name: "Save description" }));

    await waitFor(() => {
      const write = instance.calls.find((call) => call.method === "PATCH");
      expect(write?.headers.get("If-Match")).toBe("2026-09-02T10:00:00Z");
    });
  });

  it("says what the instance said when the machine is not there", async () => {
    view({ "GET /api/machines/web-01": { status: 404, body: { detail: "No machine web-01." } } });

    expect(await screen.findByText("No machine web-01.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "All machines" })).toHaveAttribute("href", "/machines");
  });
});
