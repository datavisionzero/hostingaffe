import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import type { Schemas } from "@/api/client";
import { installInstance, renderAt } from "@/shared/testing";
import { HostingMapView } from "./HostingMapView";
import { findOnMap, mapDiagram } from "./hostingMapData";
import { layoutDiagram } from "./diagramLayout";

const diagram = vi.hoisted(() => ({ fails: true }));

vi.mock("./HostingDiagram", () => ({
  HostingDiagram: ({ focus }: { focus: { id: string; at: number } | null }) => {
    if (diagram.fails) throw new Error("Diagram failed to render");
    return <div role="region" aria-label="Provider to machine diagram" data-focus={focus?.id} data-at={focus?.at} />;
  },
}));

const map: Schemas["HostingMap"] = {
  providers: [
    { key: "example-host", name: "Example Host" },
    { key: "empty-host", name: "Empty Host" },
  ],
  machines: [
    { key: "guest", name: "Guest VM", kind: "vm", status: "active", provider: "example-host", ipv4: null, ipv6: null, private_ip: "198.51.100.20" },
    { key: "host", name: "Physical host", kind: "dedicated", status: "active", provider: "example-host", ipv4: "192.0.2.10", ipv6: "2001:db8::10", private_ip: "198.51.100.10" },
    { key: "local", name: "Local box", kind: "local", status: "retired", provider: null, ipv4: null, ipv6: null, private_ip: null },
  ],
};

afterEach(() => {
  vi.unstubAllGlobals();
  diagram.fails = true;
});

it("draws only provider-to-machine edges, including inherited VM providers", () => {
  const graph = mapDiagram(map);
  expect(graph.items.map((item) => item.id)).toEqual([
    "provider:example-host", "provider:empty-host", "machine:guest", "machine:host", "machine:local",
  ]);
  expect(graph.edges.map((edge) => [edge.source, edge.target])).toEqual([
    ["provider:example-host", "machine:guest"],
    ["provider:example-host", "machine:host"],
  ]);
  const positioned = new Map(layoutDiagram(graph.items, graph.edges).map((node) => [node.id, node.position]));
  expect(positioned.get("provider:example-host")!.x).toBeLessThan(positioned.get("machine:guest")!.x);
  expect(positioned.get("machine:local")).toBeDefined();
});

it("keeps every address and navigation link in the list when the diagram fails", async () => {
  const error = vi.spyOn(console, "error").mockImplementation(() => {});
  installInstance({ "GET /api/hosting-map": map });
  renderAt("/hosting-map", <HostingMapView />);

  const list = await screen.findByRole("region", { name: "Providers and machines" });
  expect(await screen.findByText("The diagram is unavailable. The grouped list has every provider and machine.")).toBeInTheDocument();
  expect(within(list).getByRole("link", { name: "Example Host" })).toHaveAttribute("href", "/providers/example-host");
  expect(within(list).getByRole("link", { name: "guest" })).toHaveAttribute("href", "/machines/guest");
  expect(within(list).getByRole("link", { name: "host" })).toHaveAttribute("href", "/machines/host");
  expect(within(list).getByText("192.0.2.10 · 2001:db8::10 · 198.51.100.10")).toBeInTheDocument();
  expect(within(list).getByText("Unassigned machines")).toBeInTheDocument();
  expect(within(list).getByText("No machines assigned.")).toBeInTheDocument();
  expect(within(list).getByRole("link", { name: "local" })).toHaveAttribute("href", "/machines/local");
  expect(within(list).getAllByRole("link", { name: "Installations" })[0]).toHaveAttribute("href", "/installations?machine=guest");
  error.mockRestore();
});

it("answers an empty map", async () => {
  installInstance({ "GET /api/hosting-map": { providers: [], machines: [] } });
  renderAt("/hosting-map", <HostingMapView />);
  expect(await screen.findByText("No providers or machines yet.")).toBeInTheDocument();
});

it("shows a failed read with a way to the ordinary machine list", async () => {
  installInstance({ "GET /api/hosting-map": { status: 503, body: { detail: "Map unavailable." } } });
  renderAt("/hosting-map", <HostingMapView />);
  expect(await screen.findByText("Map unavailable.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "All machines" })).toHaveAttribute("href", "/machines");
});

it("hides the grouped list on request and gives the diagram the whole width", async () => {
  diagram.fails = false;
  installInstance({ "GET /api/hosting-map": map });
  renderAt("/hosting-map", <HostingMapView />);
  await screen.findByRole("region", { name: "Provider to machine diagram" });

  const toggle = screen.getByRole("button", { name: "Hide list" });
  expect(toggle).toHaveAttribute("aria-expanded", "true");
  expect(toggle).toHaveAttribute("aria-controls", "hosting-map-list");
  const list = screen.getByRole("region", { name: "Providers and machines" });
  const layout = list.parentElement!;
  expect(layout).toHaveAttribute("data-list", "shown");
  expect(layout.className).toContain("xl:grid-cols-");

  await userEvent.click(toggle);
  expect(screen.queryByRole("region", { name: "Providers and machines" })).toBeNull();
  expect(list).not.toBeVisible();
  expect(layout).toHaveAttribute("data-list", "hidden");
  expect(layout.className).not.toContain("xl:grid-cols-");
  expect(screen.getByRole("button", { name: "Show list" })).toHaveAttribute("aria-expanded", "false");

  screen.getByRole("button", { name: "Show list" }).focus();
  await userEvent.keyboard("{Enter}");
  expect(screen.getByRole("region", { name: "Providers and machines" })).toBeVisible();
  expect(within(list).getByRole("link", { name: "Example Host" })).toHaveAttribute("href", "/providers/example-host");
  expect(layout).toHaveAttribute("data-list", "shown");
});

it("offers no way to hide the list when the diagram cannot render", async () => {
  const error = vi.spyOn(console, "error").mockImplementation(() => {});
  installInstance({ "GET /api/hosting-map": map });
  renderAt("/hosting-map", <HostingMapView />);
  await screen.findByText("The diagram is unavailable. The grouped list has every provider and machine.");
  expect(screen.getByRole("region", { name: "Providers and machines" })).toBeVisible();
  expect(screen.queryByRole("button", { name: /list/ })).toBeNull();
  expect(screen.queryByRole("button", { name: "Focus map" })).toBeNull();
  error.mockRestore();
});

it("has no list control on an empty map", async () => {
  installInstance({ "GET /api/hosting-map": { providers: [], machines: [] } });
  renderAt("/hosting-map", <HostingMapView />);
  await screen.findByText("No providers or machines yet.");
  expect(screen.queryByRole("button", { name: /list/ })).toBeNull();
});

const twins: Schemas["HostingMap"] = {
  ...map,
  machines: [...map.machines, { key: "guest-2", name: "Guest VM", kind: "vm", status: "active", provider: "empty-host", ipv4: null, ipv6: null, private_ip: null }],
};

it("finds providers and machines by name or key and tells equal names apart by key", () => {
  expect(findOnMap(twins, "  ")).toEqual([]);
  expect(findOnMap(twins, "EXAMPLE").map((match) => [match.kind, match.key])).toEqual([["Provider", "example-host"]]);
  expect(findOnMap(twins, "host").map((match) => match.id)).toEqual([
    "provider:example-host", "provider:empty-host", "machine:host",
  ]);
  expect(findOnMap(twins, "guest vm").map((match) => [match.name, match.key])).toEqual([
    ["Guest VM", "guest"], ["Guest VM", "guest-2"],
  ]);
});

it("brings a found node into view without narrowing the map or the list", async () => {
  diagram.fails = false;
  installInstance({ "GET /api/hosting-map": twins });
  renderAt("/hosting-map", <HostingMapView />);
  const canvas = await screen.findByRole("region", { name: "Provider to machine diagram" });
  const find = screen.getByRole("searchbox", { name: "Find a provider or machine" });

  await userEvent.type(find, "guest vm");
  const matches = screen.getByRole("list", { name: "Matches" });
  const buttons = within(matches).getAllByRole("button");
  expect(buttons.map((button) => button.textContent)).toEqual(["MachineGuest VMguest", "MachineGuest VMguest-2"]);

  await userEvent.click(buttons[1]);
  expect(canvas).toHaveAttribute("data-focus", "machine:guest-2");
  expect(screen.queryByRole("list", { name: "Matches" })).toBeNull();
  const list = screen.getByRole("region", { name: "Providers and machines" });
  expect(within(list).getByRole("link", { name: "guest" })).toBeInTheDocument();
  expect(within(list).getByRole("link", { name: "local" })).toBeInTheDocument();

  await userEvent.clear(find);
  await userEvent.type(find, "empty");
  await userEvent.keyboard("{ArrowDown}");
  expect(screen.getByRole("button", { name: /Empty Host/ })).toHaveFocus();
  await userEvent.keyboard("{Enter}");
  expect(canvas).toHaveAttribute("data-focus", "provider:empty-host");

  await userEvent.clear(find);
  await userEvent.type(find, "physical{Enter}");
  expect(canvas).toHaveAttribute("data-focus", "machine:host");
  const at = canvas.getAttribute("data-at");
  await userEvent.keyboard("{Enter}");
  expect(canvas.getAttribute("data-at")).not.toBe(at);
});

it("says when nothing matches and closes the matches on Escape", async () => {
  diagram.fails = false;
  installInstance({ "GET /api/hosting-map": map });
  renderAt("/hosting-map", <HostingMapView />);
  const canvas = await screen.findByRole("region", { name: "Provider to machine diagram" });
  const find = screen.getByRole("searchbox", { name: "Find a provider or machine" });

  await userEvent.type(find, "nowhere{Enter}");
  expect(screen.getByRole("status")).toHaveTextContent("No provider or machine matches “nowhere”.");
  expect(canvas).not.toHaveAttribute("data-focus");

  await userEvent.keyboard("{Escape}");
  expect(screen.queryByRole("status")).toBeNull();
  expect(find).toHaveFocus();
  expect(find).toHaveAttribute("aria-expanded", "false");
});

it("offers no find action when the diagram cannot render", async () => {
  const error = vi.spyOn(console, "error").mockImplementation(() => {});
  installInstance({ "GET /api/hosting-map": map });
  renderAt("/hosting-map", <HostingMapView />);
  await screen.findByText("The diagram is unavailable. The grouped list has every provider and machine.");
  expect(screen.queryByRole("searchbox")).toBeNull();
  error.mockRestore();
});

it("gives the diagram a focus view and restores the ordinary layout on leaving it", async () => {
  diagram.fails = false;
  installInstance({ "GET /api/hosting-map": map });
  renderAt("/hosting-map", <HostingMapView />);
  await screen.findByRole("region", { name: "Provider to machine diagram" });
  await userEvent.click(screen.getByRole("button", { name: "Hide list" }));

  await userEvent.click(screen.getByRole("button", { name: "Focus map" }));
  const view = await screen.findByRole("dialog", { name: "Hosting map" });
  expect(within(view).getByRole("region", { name: "Provider to machine diagram" })).toBeInTheDocument();
  expect(within(view).getByRole("searchbox", { name: "Find a provider or machine" })).toBeInTheDocument();
  const toggle = within(view).getByRole("button", { name: "Show list" });
  expect(toggle).toHaveAttribute("aria-controls", "hosting-map-focus-list");
  await userEvent.click(toggle);
  const list = within(view).getByRole("region", { name: "Providers and machines" });
  expect(within(list).getByRole("link", { name: "local" })).toHaveAttribute("href", "/machines/local");

  await userEvent.click(within(view).getByRole("button", { name: "Exit focus" }));
  await vi.waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  expect(screen.getByRole("button", { name: "Show list" })).toHaveAttribute("aria-expanded", "false");
  expect(screen.getByRole("region", { name: "Provider to machine diagram" })).toBeInTheDocument();
});

it("leaves the focus view on Escape, after closing any open matches first", async () => {
  diagram.fails = false;
  installInstance({ "GET /api/hosting-map": map });
  renderAt("/hosting-map", <HostingMapView />);
  await screen.findByRole("region", { name: "Provider to machine diagram" });
  const enter = screen.getByRole("button", { name: "Focus map" });

  await userEvent.click(enter);
  const view = await screen.findByRole("dialog", { name: "Hosting map" });
  await userEvent.type(within(view).getByRole("searchbox"), "guest");
  await userEvent.keyboard("{Escape}");
  expect(within(view).queryByRole("list", { name: "Matches" })).toBeNull();
  expect(screen.getByRole("dialog", { name: "Hosting map" })).toBeInTheDocument();

  await userEvent.keyboard("{Escape}");
  await vi.waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  await vi.waitFor(() => expect(screen.getByRole("button", { name: "Focus map" })).toHaveFocus());
  expect(screen.getByRole("region", { name: "Providers and machines" })).toBeVisible();
});

it("shows the whole list in the focus view when the diagram fails there", async () => {
  const error = vi.spyOn(console, "error").mockImplementation(() => {});
  diagram.fails = false;
  installInstance({ "GET /api/hosting-map": map });
  renderAt("/hosting-map", <HostingMapView />);
  await screen.findByRole("region", { name: "Provider to machine diagram" });

  diagram.fails = true;
  await userEvent.click(screen.getByRole("button", { name: "Focus map" }));
  const view = await screen.findByRole("dialog", { name: "Hosting map" });
  expect(await within(view).findByText("The diagram is unavailable. The grouped list has every provider and machine.")).toBeInTheDocument();
  expect(within(view).getByRole("region", { name: "Providers and machines" })).toBeVisible();
  expect(within(view).queryByRole("button", { name: /list/ })).toBeNull();
  expect(within(view).queryByRole("searchbox")).toBeNull();
  expect(within(view).getByRole("button", { name: "Exit focus" })).toBeInTheDocument();
  error.mockRestore();
});
