import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";
import type { Schemas } from "@/api/client";
import { installInstance, renderAt } from "@/shared/testing";
import { InstallationMapView } from "./InstallationMapView";
import { domainsOf, installationDiagram } from "./installationMapData";
import { layoutDiagram } from "./diagramLayout";

vi.mock("./InstallationDiagram", () => ({
  InstallationDiagram: ({ items }: { items: { id: string; data: Record<string, unknown> }[] }) => {
    if (items.some((item) => item.id === "installation:fail")) throw new Error("Diagram failed to render");
    return <div role="img" aria-label="Mock installation diagram">{items.map((item) =>
      <div key={item.id}>{item.id}: {(item.data.domains as string[] | undefined)?.join(", ")}</div>)}</div>;
  },
}));

const map: Schemas["InstallationMap"] = {
  machine: "host", name: "Example host", status: "active",
  installations: [
    { key: "new", name: "New site", software: "site", role: "application", status: "active",
      urls: ["https://one.example.test/a", "https://two.example.test", "https://one.example.test/b"],
      latest_deployment_at: "2026-06-01T10:00:00Z" },
    { key: "old", name: "Old site", software: "site", role: "application", status: "retired",
      urls: [], latest_deployment_at: "2026-01-01T10:00:00Z" },
    { key: "proxy", name: "Proxy", software: "proxy", role: "platform", status: "active",
      urls: [], latest_deployment_at: null },
  ],
};

function view(answer: unknown = map, status = 200) {
  const instance = installInstance({ "GET /api/machines/host/installation-map": { status, body: answer } });
  renderAt("/machines/host/installation-map", <Routes>
    <Route path="/machines/:key/installation-map" element={<InstallationMapView />} />
  </Routes>);
  return instance;
}

afterEach(() => vi.unstubAllGlobals());

it("derives every distinct domain from the recorded URLs and draws only machine-to-installation edges", () => {
  expect(domainsOf(map.installations[0].urls)).toEqual(["one.example.test", "two.example.test"]);
  const collapsed = installationDiagram(map, false);
  expect(collapsed.items.map((item) => item.id)).toEqual(["machine:host", "installation:new", "installation:old"]);
  expect(collapsed.items[1].data.domains).toEqual(["one.example.test", "two.example.test"]);
  expect(collapsed.edges.map((edge) => [edge.source, edge.target])).toEqual([
    ["machine:host", "installation:new"], ["machine:host", "installation:old"],
  ]);
  const expanded = installationDiagram(map, true);
  expect(expanded.items.map((item) => item.id)).toContain("installation:proxy");
  expect(expanded.edges.map((edge) => edge.target)).toContain("installation:proxy");
});

it("lays out a dense machine while keeping platform entries available to expand", () => {
  const dense: Schemas["InstallationMap"] = {
    ...map,
    installations: Array.from({ length: 50 }, (_, index) => ({
      key: `entry-${index}`, name: `Entry ${index}`, software: "site",
      role: index < 40 ? "application" : "platform", status: "active", urls: [], latest_deployment_at: null,
    })),
  };
  const collapsed = installationDiagram(dense, false);
  expect(collapsed.items).toHaveLength(41);
  const positions = layoutDiagram(collapsed.items, collapsed.edges);
  expect(positions).toHaveLength(41);
  expect(positions.every((node) => Number.isFinite(node.position.x) && Number.isFinite(node.position.y))).toBe(true);
  expect(installationDiagram(dense, true).items).toHaveLength(51);
});

it("keeps the full sorted list beside a default-collapsed platform group and expands it", async () => {
  const user = userEvent.setup();
  const instance = view();
  const list = await screen.findByRole("region", { name: "All installations" });
  expect(instance.calls.map((call) => new URL(call.url).pathname)).toEqual(["/api/machines/host/installation-map"]);
  expect(within(list).getAllByRole("link").map((link) => link.textContent)).toEqual(["New site", "Old site", "Proxy"]);
  expect(within(list).getByRole("link", { name: "Proxy" })).toHaveAttribute("href", "/installations/proxy");
  expect(within(list).getByText("No deployment recorded")).toBeInTheDocument();
  expect(within(list).getByText("one.example.test")).toBeInTheDocument();
  expect(within(list).getByText("two.example.test")).toBeInTheDocument();
  expect(screen.queryByText(/installation:proxy/)).not.toBeInTheDocument();
  const toggle = screen.getByRole("button", { name: "Show platform installations (1)" });
  expect(toggle).toHaveAttribute("aria-expanded", "false");
  await user.click(toggle);
  expect(screen.getByRole("button", { name: "Hide platform installations (1)" })).toHaveAttribute("aria-expanded", "true");
  expect(await screen.findByText(/installation:proxy/)).toBeInTheDocument();
});

it("explains an empty machine", async () => {
  view({ machine: "host", name: "Example host", status: "active", installations: [] });
  expect(await screen.findByText("No installations recorded on this machine.")).toBeInTheDocument();
});

it("shows a failed read with a path back to the machine", async () => {
  view({ detail: "Map unavailable." }, 503);
  expect(await screen.findByText("Map unavailable.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Machine details" })).toHaveAttribute("href", "/machines/host");
});

it("keeps the full list usable if the diagram fails", async () => {
  const error = vi.spyOn(console, "error").mockImplementation(() => {});
  view({ ...map, installations: [{ ...map.installations[0], key: "fail" }] });
  expect(await screen.findByText("The diagram is unavailable. The installation list remains complete.")).toBeInTheDocument();
  expect(within(screen.getByRole("region", { name: "All installations" })).getByRole("link", { name: "New site" }))
    .toHaveAttribute("href", "/installations/fail");
  error.mockRestore();
});
