import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useLocation } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/Session";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { SettingsView } from "./SettingsView";

afterEach(() => vi.unstubAllGlobals());

const anAgent = {
  id: "0199a000-0000-7000-8000-000000000009",
  name: "one",
  token: { prefix: "ha_wxyz", created_at: "2026-09-02T10:00:00Z", revoked_at: null },
};

/** Where the click left the reader. */
function At() {
  return <span data-testid="at">{useLocation().pathname}</span>;
}

function settings(routes: Parameters<typeof installInstance>[0] = {}, at = "/settings") {
  const instance = installInstance({
    "GET /sessions": [],
    "GET /tokens": [],
    "GET /agents": [],
    ...routes,
  });

  renderAt(at, <SessionProvider value={{ me: aUser, signOut: vi.fn() }}><Routes><Route path="/settings/*" element={<SettingsView />} /></Routes><At /></SessionProvider>);
  return instance;
}

// The form is read off the event, and React empties `currentTarget` once the
// event has been dispatched: reading it back after the await threw, and the
// `TypeError` was reported where "Saved." belonged.
it("reports a changed password as saved and clears the fields", async () => {
  settings({ "POST /me/password": { status: 204 } }, "/settings/security");
  const user = userEvent.setup();

  await user.type(await screen.findByLabelText("Current password"), "a long first password");
  await user.type(screen.getByLabelText("New password"), "a long second password");
  await user.click(screen.getByRole("button", { name: "Change password" }));

  expect(await screen.findByRole("status")).toHaveTextContent("Saved.");
  expect(screen.getByLabelText("Current password")).toHaveValue("");
  expect(screen.getByLabelText("New password")).toHaveValue("");
});

it("reports a created agent as saved, shows its secret once and clears the name", async () => {
  settings({ "POST /agents": { status: 201, body: { ...anAgent, token: { ...anAgent.token, secret: "ha_thesecret" } } } }, "/settings/agents");
  const user = userEvent.setup();

  await user.type(await screen.findByLabelText("Agent name"), "one");
  await user.click(screen.getByRole("button", { name: "Create agent" }));

  expect(await screen.findByText("ha_thesecret")).toBeInTheDocument();
  expect(screen.getByLabelText("Agent name")).toHaveValue("");
  expect(screen.getAllByRole("status").map((x) => x.textContent)).toContain("Saved.");
});

const aToken = { id: "0199a000-0000-7000-8000-00000000000a", prefix: "ha_abcd", created_at: "2026-09-02T10:00:00Z", revoked_at: null };
const refusal = (status: number, detail: string) => ({ status, body: { type: "about:blank", title: "refused", status, detail } });

// The revokes were `.then(load)`: the list reloaded unchanged, and a token
// that could not be revoked looked exactly like one that was.
it("says why a token could not be revoked", async () => {
  settings({
    "GET /tokens": [aToken],
    [`DELETE /tokens/${aToken.id}`]: refusal(409, "The token was already revoked."),
  }, "/settings/tokens");
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Revoke" }));

  expect(await screen.findByRole("status")).toHaveTextContent("The token was already revoked.");
});

it("reports a revoked token and reloads the list", async () => {
  let revoked = false;
  settings({
    "GET /tokens": () => (revoked ? [{ ...aToken, revoked_at: "2026-09-04T10:00:00Z" }] : [aToken]),
    [`DELETE /tokens/${aToken.id}`]: () => { revoked = true; return { status: 204 }; },
  }, "/settings/tokens");
  const user = userEvent.setup();

  await user.click(await screen.findByRole("button", { name: "Revoke" }));

  expect(await screen.findByRole("status")).toHaveTextContent("Token revoked.");
  expect(screen.queryByRole("button", { name: "Revoke" })).not.toBeInTheDocument();
});

// `/settings` was one address for five subjects; it is now the way in to the
// first of them, and still a link that works.
it("lands on the first area when no area is named", async () => {
  settings();

  expect(await screen.findByRole("heading", { name: "Profile" })).toBeInTheDocument();
  expect(screen.queryByLabelText("Current password")).not.toBeInTheDocument();
});

it("gives each area an address of its own", async () => {
  settings({}, "/settings/tokens");

  expect(await screen.findByRole("heading", { name: "User tokens" })).toBeInTheDocument();
  expect(screen.queryByRole("heading", { name: "Profile" })).not.toBeInTheDocument();
});

// The nav entries were relative, and a relative link inside a splat route
// resolves against the whole address the splat matched: from
// `/settings/security` the "User tokens" entry led to
// `/settings/security/tokens`, which matches no area at all. Every click added
// a segment, and after the first one no other area could be reached.
it("moves between areas from an area, without growing the address", async () => {
  settings({}, "/settings/security");
  const user = userEvent.setup();

  await user.click(await screen.findByRole("link", { name: "User tokens" }));

  expect(screen.getByTestId("at")).toHaveTextContent("/settings/tokens");
  expect(await screen.findByRole("heading", { name: "User tokens" })).toBeInTheDocument();

  await user.click(screen.getByRole("link", { name: "Profile" }));

  expect(screen.getByTestId("at")).toHaveTextContent("/settings/profile");
  expect(await screen.findByRole("heading", { name: "Profile" })).toBeInTheDocument();
});
