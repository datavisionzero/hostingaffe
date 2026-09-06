import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { PagesView } from "./PagesView";
import { NewPageView, PageView } from "./PageView";

afterEach(() => vi.unstubAllGlobals());

const page = {
  slug: "architecture",
  title: "Architecture",
  body: "# The four layers\n\nDependencies point inward and only inward.",
  author: { id: "0199a000-0000-7000-8000-000000000001", kind: "user", name: "maintainer" },
  updated_by: { id: "0199a000-0000-7000-8000-000000000002", kind: "agent", name: "quiet-otter-42" },
  created_at: "2026-09-05T10:00:00Z",
  updated_at: "2026-09-05T12:00:00Z",
};

const summary = {
  slug: "architecture",
  title: "Architecture",
  updated_by: { id: "0199a000-0000-7000-8000-000000000002", kind: "agent", name: "quiet-otter-42" },
  created_at: "2026-09-05T10:00:00Z",
  updated_at: "2026-09-05T12:00:00Z",
};

function renderPage(routes: Parameters<typeof installInstance>[0] = {}) {
  const instance = installInstance({ "GET /pages/architecture": { body: page }, ...routes });
  renderAt("/pages/architecture", <Routes><Route path="/pages/:slug" element={<PageView />} /></Routes>);
  return instance;
}

it("lists the wiki flat, by slug, and says who touched what last", async () => {
  installInstance({ "GET /pages": [summary] });
  renderAt("/pages", <Routes><Route path="/pages" element={<PagesView />} /></Routes>);

  expect(await screen.findByRole("link", { name: /architecture/ })).toHaveAttribute("href", "/pages/architecture");
  expect(screen.getByText("quiet-otter-42", { exact: false })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "New page" })).toHaveAttribute("href", "/pages/new");
});

// The filter lives in the URL, and a filter that matched nothing is a
// different state from a wiki nobody has written in yet.
it("filters out of the URL and says which empty it is", async () => {
  const instance = installInstance({
    "GET /pages": (request) =>
      new URL(request.url).searchParams.get("q") === "nothing" ? [] : [summary],
  });
  renderAt("/pages?q=nothing", <Routes><Route path="/pages" element={<PagesView />} /></Routes>);

  expect(await screen.findByText("Nothing matches.")).toBeInTheDocument();
  const asked = instance.calls.find((call) => new URL(call.url).pathname === "/pages")!;
  expect(new URL(asked.url).searchParams.get("q")).toBe("nothing");
});

// The wiki is flat, so an empty one has to say what a page is for; a list that
// is simply empty teaches nobody what the screen is.
it("says what a page is for while there are none", async () => {
  installInstance({ "GET /pages": [] });
  renderAt("/pages", <Routes><Route path="/pages" element={<PagesView />} /></Routes>);

  expect(await screen.findByText("No pages yet.")).toBeInTheDocument();
});

// The wiki is flat because the search replaces the navigation a tree would
// have been, so the search stands over the list rather than behind a sheet.
it("searches the wiki out of the URL, and says when nothing matched", async () => {
  const instance = installInstance({
    "GET /pages": (request) =>
      new URL(request.url).searchParams.get("q") === "inward" ? [summary] : [],
  });
  renderAt("/pages", <Routes><Route path="/pages" element={<PagesView />} /></Routes>);
  const user = userEvent.setup();

  await user.type(await screen.findByLabelText("Search"), "inward");

  expect(await screen.findByRole("link", { name: /architecture/ })).toBeInTheDocument();
  await vi.waitFor(() => {
    const asked = instance.calls.map((call) => new URL(call.url)).find((url) => url.searchParams.get("q") === "inward");
    expect(asked?.pathname).toBe("/pages");
  });

  await user.clear(screen.getByLabelText("Search"));
  await user.type(screen.getByLabelText("Search"), "nothing");

  expect(await screen.findByText("Nothing matches.")).toBeInTheDocument();
});

it("opens the page itself: the Markdown and who changed it last", async () => {
  renderPage();

  expect(await screen.findByRole("heading", { name: "The four layers" })).toBeInTheDocument();
  expect(screen.getByText("Dependencies point inward and only inward.")).toBeInTheDocument();
  expect(screen.getByText("maintainer")).toBeInTheDocument();
});

it("edits the body under If-Match", async () => {
  const instance = renderPage({ "PATCH /pages/architecture": { body: { ...page, body: "Rewritten." } } });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Edit" }));
  await user.clear(await screen.findByLabelText("Body"));
  await user.type(await screen.findByLabelText("Body"), "Rewritten.");
  await user.click(screen.getByRole("button", { name: "Save changes" }));

  const write = await vi.waitFor(() => instance.calls.find((call) => call.method === "PATCH")!);
  expect(write.headers.get("If-Match")).toBe("2026-09-05T12:00:00Z");
  expect(await screen.findByText("Rewritten.")).toBeInTheDocument();
});

/**
 * The conflict has to be visible and the typed text has to survive it: that is
 * what the header is for (`docs/api.md`, Concurrency on text fields).
 */
it("keeps what was typed when somebody came between, and shows what they wrote", async () => {
  const theirs = { ...page, body: "Their version.", updated_at: "2026-09-05T13:00:00Z" };
  const instance = renderPage({
    "PATCH /pages/architecture": (request) =>
      request.headers.get("If-Match") === "2026-09-05T12:00:00Z"
        ? { status: 412, body: { type: "/problems/stale", title: "stale", status: 412, detail: "It changed.", current: theirs } }
        : { body: { ...page, body: "Mine." } },
  });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Edit" }));
  await user.clear(await screen.findByLabelText("Body"));
  await user.type(await screen.findByLabelText("Body"), "Mine.");
  await user.click(screen.getByRole("button", { name: "Save changes" }));

  const alert = await screen.findByRole("alert");
  expect(alert).toHaveTextContent("architecture was changed while you were editing it.");
  expect(alert).toHaveTextContent("Their version.");
  // The typed text is still in the field, so saving again is a decision.
  expect(screen.getByLabelText("Body")).toHaveValue("Mine.");

  await user.click(screen.getByRole("button", { name: "Save changes" }));

  // The version the refusal handed back is what the second attempt is guarded with.
  const second = await vi.waitFor(() => {
    const writes = instance.calls.filter((call) => call.method === "PATCH");
    expect(writes).toHaveLength(2);
    return writes[1]!;
  });
  expect(second.headers.get("If-Match")).toBe("2026-09-05T13:00:00Z");
});

/**
 * Renaming moves the address and nothing forwards (ADR 0021), so it is an act
 * with a warning rather than a field among four.
 */
it("warns that nothing forwards before it renames a page", async () => {
  const instance = renderPage({
    "PATCH /pages/architecture": { body: { ...page, slug: "betriebshandbuch" } },
  });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Rename page" }));
  const dialog = screen.getByRole("dialog", { name: "Rename architecture?" });
  expect(dialog).toHaveTextContent("the old slug leads nowhere afterwards");

  const field = within(dialog).getByLabelText("New slug");
  await user.clear(field);
  await user.type(field, "betriebshandbuch");
  await user.click(within(dialog).getByRole("button", { name: "Rename page" }));

  const write = await vi.waitFor(() => instance.calls.find((call) => call.method === "PATCH")!);
  expect(await write.json()).toEqual({ slug: "betriebshandbuch" });
});

it("says the slug is held while a deleted page can come back", async () => {
  const instance = renderPage({
    "DELETE /pages/architecture": { status: 204 },
    "POST /pages/architecture/restore": { body: page },
  });
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Delete page" }));
  const dialog = screen.getByRole("dialog", { name: "Delete architecture?" });
  expect(dialog).toHaveTextContent("Its slug stays taken until then");
  await user.click(within(dialog).getByRole("button", { name: "Delete page" }));

  await user.click(await screen.findByRole("button", { name: "Restore architecture" }));
  expect(await screen.findByRole("heading", { name: "The four layers" })).toBeInTheDocument();
  expect(instance.calls.some((call) => call.method === "POST")).toBe(true);
});

/** The slug is given, never derived from the title (ADR 0021). */
it("asks for the slug when a page is created", async () => {
  const instance = installInstance({
    "POST /pages": { status: 201, body: page },
  });
  renderAt("/pages/new", <Routes><Route path="/pages/new" element={<NewPageView />} /></Routes>);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Slug"), "architecture");
  await user.type(screen.getByLabelText("Title"), "Architecture");
  await user.type(await screen.findByLabelText("Body"), "# The four layers");
  await user.click(screen.getByRole("button", { name: "Create page" }));

  const write = await vi.waitFor(() => instance.calls.find((call) => call.method === "POST")!);
  expect(await write.json()).toMatchObject({ slug: "architecture", title: "Architecture", body: "# The four layers" });
});
