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
  status: "active",
  measured_at: null,
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

  it("says when the fields were last measured, and says so when they never were", async () => {
    view();

    expect(
      await screen.findByText("These fields have never been measured against the machine itself."),
    ).toBeInTheDocument();
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
