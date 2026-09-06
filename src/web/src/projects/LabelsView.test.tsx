import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { LabelsView } from "./LabelsView";

afterEach(() => vi.unstubAllGlobals());

it("creates a label, its group chosen from the ones the project has", async () => {
  const instance = installInstance({
    "GET /projects/PLAN/labels": [{ name: "api", group: "area", description: "The HTTP surface" }],
    "POST /projects/PLAN/labels": {
      status: 201,
      body: { name: "web", group: "area", description: "Browser application" },
    },
  });
  renderAt("/PLAN/labels", <Routes><Route path="/:project/labels" element={<LabelsView />} /></Routes>);
  const user = userEvent.setup();

  await screen.findByRole("button", { name: "Create" });
  await user.type(screen.getByLabelText("Name"), "web");
  await user.click(screen.getByRole("combobox", { name: "Group" }));
  await user.click(screen.getByRole("option", { name: /^area/ }));
  await user.type(screen.getByLabelText("Description"), "Browser application");
  await user.click(screen.getByRole("button", { name: "Create" }));

  expect(await vi.waitFor(() => instance.calls.some((call) => call.method === "POST"))).toBe(true);
  const request = instance.calls.find((call) => call.method === "POST")!;
  expect(await request.json()).toEqual({ name: "web", group: "area", description: "Browser application" });
  expect(screen.getByLabelText("Name")).toHaveValue("");
});

// A typo used to become a new group in silence, and the group is what carries
// the exclusion two labels were supposed to have.
it("names a new group only as a step of its own", async () => {
  const instance = installInstance({
    "GET /projects/PLAN/labels": [{ name: "api", group: "area", description: null }],
    "POST /projects/PLAN/labels": { status: 201, body: { name: "web", group: "surface", description: null } },
  });
  renderAt("/PLAN/labels", <Routes><Route path="/:project/labels" element={<LabelsView />} /></Routes>);
  const user = userEvent.setup();

  await user.type(await screen.findByLabelText("Name"), "web");
  await user.type(screen.getByRole("combobox", { name: "Group" }), "surface");
  await user.click(screen.getByRole("option", { name: "New group surface" }));
  await user.click(screen.getByRole("button", { name: "Create" }));

  expect(await vi.waitFor(() => instance.calls.some((call) => call.method === "POST"))).toBe(true);
  expect(await instance.calls.find((call) => call.method === "POST")!.json()).toEqual({
    name: "web", group: "surface", description: null,
  });
});

// The name is not the key — a label has its own id, and `PATCH` takes all
// three fields — so a name typed once was never a reason to keep it forever.
it("edits the name, the group and the description in one dialog", async () => {
  const label = { name: "web", group: "area", description: "Browser application" };
  const instance = installInstance({
    "GET /projects/PLAN/labels": [label],
    "PATCH /projects/PLAN/labels/web": { body: { name: "browser", group: null, description: "Web application" } },
  });
  renderAt("/PLAN/labels", <Routes><Route path="/:project/labels" element={<LabelsView />} /></Routes>);
  const user = userEvent.setup();

  const row = (await screen.findByText("web")).closest("li")!;
  await user.click(within(row).getByRole("button", { name: "Edit" }));
  const dialog = screen.getByRole("dialog", { name: "Edit web" });
  await user.clear(within(dialog).getByLabelText("Name"));
  await user.type(within(dialog).getByLabelText("Name"), "browser");
  await user.click(within(dialog).getByRole("combobox", { name: "Group" }));
  await user.click(within(dialog).getByRole("option", { name: /No group/ }));
  await user.clear(within(dialog).getByLabelText("Description"));
  await user.type(within(dialog).getByLabelText("Description"), "Web application");
  await user.click(within(dialog).getByRole("button", { name: "Save label" }));

  expect(await vi.waitFor(() => instance.calls.some((call) => call.method === "PATCH"))).toBe(true);
  const request = instance.calls.find((call) => call.method === "PATCH")!;
  expect(await request.json()).toEqual({ name: "browser", group: null, description: "Web application" });
});

// The server names the issues and the epics that stand in the way; reading them
// is not the same as being able to go and look.
it("shows the group refusal at the group, with what is in the way as links", async () => {
  installInstance({
    "GET /projects/PLAN/labels": [
      { name: "web", group: "area", description: null },
      { name: "api", group: "surface", description: null },
    ],
    "PATCH /projects/PLAN/labels/web": {
      status: 400,
      body: {
        type: "/problems/validation", title: "refused", status: 400,
        detail: "1 issue(s) and 1 epic(s) would carry two labels of the group surface: PLAN-4, PLAN-E2.",
        errors: { group: ["1 issue(s) and 1 epic(s) would carry two labels of this group."] },
        issues: ["PLAN-4"],
        epics: ["PLAN-E2"],
      },
    },
  });
  renderAt("/PLAN/labels", <Routes><Route path="/:project/labels" element={<LabelsView />} /></Routes>);
  const user = userEvent.setup();

  const row = (await screen.findByText("web")).closest("li")!;
  await user.click(within(row).getByRole("button", { name: "Edit" }));
  const dialog = screen.getByRole("dialog", { name: "Edit web" });
  await user.click(within(dialog).getByRole("combobox", { name: "Group" }));
  await user.click(within(dialog).getByRole("option", { name: /^surface/ }));
  await user.click(within(dialog).getByRole("button", { name: "Save label" }));

  const said = await within(dialog).findByRole("alert");
  expect(said).toHaveTextContent("1 issue(s) and 1 epic(s) would carry two labels of this group.");
  expect(within(said).getByRole("link", { name: "PLAN-4" })).toHaveAttribute("href", "/PLAN/issues/4");
  expect(within(said).getByRole("link", { name: "PLAN-E2" })).toHaveAttribute("href", "/PLAN/epics/E2");
  expect(within(dialog).getByRole("combobox", { name: "Group" })).toHaveAttribute("aria-invalid", "true");
});

// The group is a string on each label, so dissolving one is a row of writes
// rather than one — and it says how far it got.
it("dissolves a group by writing every label in it, and says what did not go through", async () => {
  const instance = installInstance({
    "GET /projects/PLAN/labels": [
      { name: "web", group: "area", description: null },
      { name: "api", group: "area", description: null },
    ],
    "PATCH /projects/PLAN/labels/web": { body: { name: "web", group: null, description: null } },
    "PATCH /projects/PLAN/labels/api": { status: 400, body: { detail: "The label api is in the way." } },
  });
  renderAt("/PLAN/labels", <Routes><Route path="/:project/labels" element={<LabelsView />} /></Routes>);
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Dissolve group" }));
  await user.click(within(screen.getByRole("dialog", { name: "Dissolve area?" })).getByRole("button", { name: "Dissolve group" }));

  expect(await screen.findByRole("alert")).toHaveTextContent("1 of 2 labels moved. api: The label api is in the way.");
  expect(instance.calls.filter((call) => call.method === "PATCH")).toHaveLength(2);
});

it("requires in-page confirmation before deleting a label", async () => {
  const label = { name: "web", group: "area", description: "Browser application" };
  const instance = installInstance({
    "GET /projects/PLAN/labels": [label],
    "DELETE /projects/PLAN/labels/web": { status: 204 },
    "POST /projects/PLAN/labels/web/restore": { body: label },
  });
  renderAt("/PLAN/labels", <Routes><Route path="/:project/labels" element={<LabelsView />} /></Routes>);
  const user = userEvent.setup();

  const row = (await screen.findByText("web")).closest("li")!;
  await user.click(within(row).getByRole("button", { name: "Delete" }));
  await user.click(within(screen.getByRole("dialog", { name: "Delete web?" })).getByRole("button", { name: "Cancel" }));
  expect(instance.calls.some((call) => call.method === "DELETE")).toBe(false);

  await user.click(within(row).getByRole("button", { name: "Delete" }));
  await user.click(within(screen.getByRole("dialog", { name: "Delete web?" })).getByRole("button", { name: "Delete label" }));
  expect(await vi.waitFor(() => instance.calls.some((call) => call.method === "DELETE"))).toBe(true);

  await user.click(await screen.findByRole("button", { name: "Restore web" }));
  expect(await vi.waitFor(() => instance.calls.some((call) => new URL(call.url).pathname.endsWith("/restore")))).toBe(true);
});

// The banner used to live inside the loaded list, so a reload that failed
// after the delete had succeeded took the only way back with it.
it("still offers the way back when the reload after a delete fails", async () => {
  const label = { name: "web", group: "area", description: "Browser application" };
  let asked = 0;
  installInstance({
    "GET /projects/PLAN/labels": () => (asked++ === 0 ? [label] : { status: 500, body: { detail: "no" } }),
    "DELETE /projects/PLAN/labels/web": { status: 204 },
  });
  renderAt("/PLAN/labels", <Routes><Route path="/:project/labels" element={<LabelsView />} /></Routes>);
  const user = userEvent.setup();

  const row = (await screen.findByText("web")).closest("li")!;
  await user.click(within(row).getByRole("button", { name: "Delete" }));
  await user.click(within(screen.getByRole("dialog", { name: "Delete web?" })).getByRole("button", { name: "Delete label" }));

  const restore = await screen.findByRole("button", { name: "Restore web" });
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  // The row the dialog sat in is gone; the offer to undo is where the keyboard
  // goes, rather than back to the top of the document. An effect puts it there
  // after the button is in the document, so this is waited for.
  await waitFor(() => expect(restore).toHaveFocus());
});

// `reload` is run after a write that already succeeded, so a reload that cannot
// reach the instance must not be reported as the delete having failed.
it("does not report a delete as failed when the reload cannot reach the instance", async () => {
  const label = { name: "web", group: "area", description: "Browser application" };
  let asked = 0;
  installInstance({
    "GET /projects/PLAN/labels": () => {
      if (asked++ > 0) throw new TypeError("Failed to fetch");
      return [label];
    },
    "DELETE /projects/PLAN/labels/web": { status: 204 },
  });
  renderAt("/PLAN/labels", <Routes><Route path="/:project/labels" element={<LabelsView />} /></Routes>);
  const user = userEvent.setup();

  const row = (await screen.findByText("web")).closest("li")!;
  await user.click(within(row).getByRole("button", { name: "Delete" }));
  await user.click(within(screen.getByRole("dialog", { name: "Delete web?" })).getByRole("button", { name: "Delete label" }));

  expect(await screen.findByRole("button", { name: "Restore web" })).toBeInTheDocument();
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  // The page says the list could not be loaded — a separate statement from the
  // delete, which the banner reports as done.
  expect(screen.getByText("The instance did not answer.")).toBeInTheDocument();
});
