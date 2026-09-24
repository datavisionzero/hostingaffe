import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import type { Schemas } from "@/api/client";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { ProviderAssignment } from "./ProviderAssignment";
import { ProviderListView } from "./ProviderListView";
import { NewProviderView, ProviderView } from "./ProviderView";

const identity = { id: aUser.id, kind: "user", name: aUser.name };
const provider = {
  key: "example-host", name: "Example Host", description: "**Support** every day.",
  emblem: null, emblem_palette: null,
  created_by: identity, updated_by: identity,
  created_at: "2026-09-02T10:00:00Z", updated_at: "2026-09-02T10:00:00Z",
};
const machine = {
  key: "host", name: "The host", kind: "dedicated", status: "active",
  provider: "example-host", host: null, legacy_provider: null,
  updated_at: "2026-09-02T10:00:00Z",
} as unknown as Schemas["Machine"];

afterEach(() => vi.unstubAllGlobals());

it("lists providers, including a usable empty state and creation route", async () => {
  installInstance({ "GET /api/providers": [] });
  renderAt("/providers", <ProviderListView />);
  expect(await screen.findByText("No providers yet.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Add provider" })).toHaveAttribute("href", "/providers/new");
});

it("creates a provider with its description", async () => {
  const instance = installInstance({
    "POST /api/providers": (request) => {
      void request;
      return { status: 201, body: { ...provider, description: "A Markdown note." } };
    },
  });
  renderAt("/providers/new", <Routes>
    <Route path="/providers/new" element={<NewProviderView />} />
    <Route path="/providers/:key" element={<p>Provider created</p>} />
  </Routes>);

  await userEvent.type(screen.getByRole("textbox", { name: "Key" }), "example-host");
  await userEvent.type(screen.getByRole("textbox", { name: "Name" }), "Example Host");
  await userEvent.type(await screen.findByRole("textbox", { name: "Description" }), "A Markdown note.");
  await userEvent.click(screen.getByRole("button", { name: "Add provider" }));

  expect(await screen.findByText("Provider created")).toBeInTheDocument();
  const call = instance.calls.find((request) => request.method === "POST")!;
  expect(await call.json()).toEqual({ key: "example-host", name: "Example Host", description: "A Markdown note." });
});

it("shows its Markdown description, machines and history links", async () => {
  installInstance({
    "GET /api/providers/example-host": provider,
    "GET /api/providers/example-host/history": [],
    "GET /api/machines": [{ key: "host", name: "The host", status: "active" }],
  });
  renderAt("/providers/example-host", <Routes><Route path="/providers/:key" element={<ProviderView />} /></Routes>);

  expect(await screen.findByText("Support")).toBeInTheDocument();
  expect(await screen.findByRole("link", { name: /host.*The host/ })).toHaveAttribute("href", "/machines/host");
});

it("keeps a draft and adopts the newer version after a stale write", async () => {
  let writes = 0;
  const instance = installInstance({
    "GET /api/providers/example-host": provider,
    "GET /api/providers/example-host/history": [],
    "GET /api/machines": [],
    "PATCH /api/providers/example-host": () => ++writes === 1
      ? { status: 412, body: { current: { ...provider, name: "Changed elsewhere", updated_at: "2026-09-03T10:00:00Z" } } }
      : { ...provider, name: "My edit", updated_at: "2026-09-04T10:00:00Z" },
  });
  renderAt("/providers/example-host", <Routes><Route path="/providers/:key" element={<ProviderView />} /></Routes>);

  await userEvent.click(await screen.findByRole("button", { name: "Edit provider" }));
  await userEvent.clear(screen.getByRole("textbox", { name: "Name" }));
  await userEvent.type(screen.getByRole("textbox", { name: "Name" }), "My edit");
  await userEvent.click(screen.getByRole("button", { name: "Save provider" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("Changed elsewhere");
  expect(screen.getByRole("textbox", { name: "Name" })).toHaveValue("My edit");
  await userEvent.click(screen.getByRole("button", { name: "Save provider" }));
  await waitFor(() => expect(writes).toBe(2));
  const patches = instance.calls.filter((request) => request.method === "PATCH");
  expect(patches[0]!.headers.get("If-Match")).toBe(provider.updated_at);
  expect(patches[1]!.headers.get("If-Match")).toBe("2026-09-03T10:00:00Z");
});

it("shows a failed provider read with a way back", async () => {
  installInstance({ "GET /api/providers/missing": { status: 404, body: { detail: "No provider missing." } } });
  renderAt("/providers/missing", <Routes><Route path="/providers/:key" element={<ProviderView />} /></Routes>);
  expect(await screen.findByText("No provider missing.")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "All providers" })).toHaveAttribute("href", "/providers");
});

it("sets and clears a non-VM assignment and links its effective provider", async () => {
  const instance = installInstance({
    "GET /api/providers": [provider],
    "PATCH /api/machines/host": { ...machine, provider: "" },
  });
  renderAt("/machines/host", <ProviderAssignment machine={machine} onWritten={() => {}} />);
  expect(screen.getByRole("link", { name: "example-host" })).toHaveAttribute("href", "/providers/example-host");
  await userEvent.click(screen.getByRole("button", { name: "Change" }));
  await userEvent.selectOptions(await screen.findByRole("combobox", { name: "Provider" }), "");
  await userEvent.click(screen.getByRole("button", { name: "Save provider" }));
  await waitFor(() => expect(instance.calls.some((call) => call.method === "PATCH")).toBe(true));
  expect(await instance.calls.find((call) => call.method === "PATCH")!.json()).toEqual({ provider: "" });
});

it("explains a VM's inherited provider and links its host", () => {
  installInstance({});
  renderAt("/machines/guest", <ProviderAssignment machine={{ ...machine, kind: "vm", key: "guest", host: "host", legacy_provider: "Old text" }} onWritten={() => {}} />);
  expect(screen.getByText(/This VM inherits/)).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "example-host" })).toHaveAttribute("href", "/providers/example-host");
  expect(screen.getByRole("link", { name: "host" })).toHaveAttribute("href", "/machines/host");
  expect(screen.getByText(/Old text/)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Change" })).not.toBeInTheDocument();
});

function aProvider(answers: Parameters<typeof installInstance>[0]) {
  const instance = installInstance({
    "GET /api/providers/example-host": provider,
    "GET /api/providers/example-host/history": [],
    "GET /api/machines": [],
    ...answers,
  });
  renderAt("/providers/example-host", <Routes><Route path="/providers/:key" element={<ProviderView />} /></Routes>);
  return instance;
}

it("writes a chosen emblem at once and reads the provider again", async () => {
  const instance = aProvider({ "PATCH /api/providers/example-host": { ...provider, emblem: "orbit" } });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Change the emblem of example-host" }));
  const compositions = await screen.findByRole("group", { name: "Composition" });
  expect(within(compositions).getAllByRole("button")).toHaveLength(16);
  expect(screen.getByText("Derived from the key until one is chosen.")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Automatic" })).toBeDisabled();

  await user.click(within(compositions).getByRole("button", { name: "orbit" }));

  await waitFor(() => expect(instance.calls.find((call) => call.method === "PATCH")).toBeDefined());
  expect(await instance.calls.find((call) => call.method === "PATCH")!.clone().json())
    .toEqual({ name: null, description: null, emblem: "orbit" });
  await waitFor(() => expect(instance.calls.filter((call) =>
    call.method === "GET" && new URL(call.url).pathname === "/api/providers/example-host")).toHaveLength(2));
});

it("clears both words when the emblem goes back to automatic", async () => {
  const instance = aProvider({
    "GET /api/providers/example-host": { ...provider, emblem: "arch", emblem_palette: "dusk" },
    "PATCH /api/providers/example-host": provider,
  });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Change the emblem of example-host" }));
  const palettes = await screen.findByRole("group", { name: "Palette" });
  expect(within(palettes).getByRole("button", { name: "dusk" })).toHaveAttribute("aria-pressed", "true");

  await user.click(screen.getByRole("button", { name: "Automatic" }));

  await waitFor(() => expect(instance.calls.find((call) => call.method === "PATCH")).toBeDefined());
  expect(await instance.calls.find((call) => call.method === "PATCH")!.clone().json())
    .toEqual({ name: null, description: null, emblem: "", emblem_palette: "" });
});
