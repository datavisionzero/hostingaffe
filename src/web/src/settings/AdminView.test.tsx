import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useLocation } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/Session";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { AdminView } from "./AdminView";

afterEach(() => vi.unstubAllGlobals());

const maintainer = { id: aUser.id, name: "maintainer", email: "maintainer@example.test", state: "active", administrator: true };
const invited = { id: "0199a000-0000-7000-8000-000000000002", name: "newcomer", email: "newcomer@example.test", state: "invited", administrator: false };

function admin(routes: Parameters<typeof installInstance>[0], at = "/admin/users") {
  const instance = installInstance({
    "GET /admin/smtp": { configured: false, host: null, port: null, security: null, sender: null },
    ...routes,
  });

  renderAt(at, <SessionProvider value={{ me: aUser, signOut: vi.fn() }}><Routes><Route path="/admin/*" element={<AdminView />} /></Routes><At /></SessionProvider>);
  return instance;
}

/** Where the click left the reader. */
function At() {
  return <span data-testid="at">{useLocation().pathname}</span>;
}

/** The row's acts live in its menu; opening it is the first half of clicking one. */
async function act(user: ReturnType<typeof userEvent.setup>, of: string, what: string) {
  await user.click(await screen.findByRole("button", { name: `Actions for ${of}` }));
  await user.click(await screen.findByRole("menuitem", { name: what }));
}

// The reload after a successful invite used to be unreachable: the form was
// read back off the event after the await, which threw before `load()` ran.
it("shows an invited user without a reload of the page", async () => {
  let sent = false;
  admin({
    "GET /users": () => (sent ? [maintainer, invited] : [maintainer]),
    "POST /users": () => { sent = true; return { status: 201, body: invited }; },
  });
  const user = userEvent.setup();

  await screen.findByText("maintainer@example.test · active · administrator");
  await user.type(screen.getByLabelText("Name"), "newcomer");
  await user.type(screen.getByLabelText("Email"), "newcomer@example.test");
  await user.click(screen.getByRole("button", { name: "Invite" }));

  expect(await screen.findByText("newcomer@example.test · invited")).toBeInTheDocument();
  expect(screen.getByLabelText("Name")).toHaveValue("");
});

// A refusal is a sentence the screen owes the reader. Both of these used to
// be `.then(load)`: the list reloaded unchanged and nothing said why.
const refusal = (status: number, detail: string) => ({ status, body: { type: "about:blank", title: "refused", status, detail } });

it("says why the last administrator cannot be demoted", async () => {
  admin({
    "GET /users": [maintainer],
    [`PATCH /users/${maintainer.id}`]: refusal(422, "Deactivation or demotion would leave no active administrator."),
  });
  const user = userEvent.setup();

  await act(user, "maintainer", "Demote");

  expect(await screen.findByRole("status")).toHaveTextContent("Deactivation or demotion would leave no active administrator.");
});

it("says why the last administrator cannot be deactivated", async () => {
  admin({
    "GET /users": [maintainer],
    [`POST /users/${maintainer.id}/deactivate`]: refusal(422, "Deactivation or demotion would leave no active administrator."),
  });
  const user = userEvent.setup();

  await act(user, "maintainer", "Deactivate");

  expect(await screen.findByRole("status")).toHaveTextContent("Deactivation or demotion would leave no active administrator.");
});

// "Invitation resent." was set from a `.then()` that never looked, so a resend
// that failed reported the opposite of what happened.
it("does not report a resent invitation that did not go out", async () => {
  admin({
    "GET /users": [maintainer, invited],
    [`POST /users/${invited.id}/invitation`]: refusal(503, "Transactional email is not configured."),
  });
  const user = userEvent.setup();

  await act(user, "newcomer", "Resend invitation");

  const notice = await screen.findByRole("status");
  expect(notice).toHaveTextContent("Transactional email is not configured.");
  expect(notice).not.toHaveTextContent("Invitation resent.");
});

it("lands on the users area when no area is named", async () => {
  admin({ "GET /users": [maintainer] }, "/admin");

  expect(await screen.findByRole("heading", { name: "Users" })).toBeInTheDocument();
});

// An area is an address, and picking another one leaves the current one
// rather than growing the address a segment at a time.
it("leaves the current area behind when another is picked", async () => {
  admin({ "GET /users": [maintainer] }, "/admin/email");
  const user = userEvent.setup();

  expect(await screen.findByRole("link", { name: "Transactional email" })).toHaveAttribute("aria-current", "page");

  await user.click(screen.getByRole("link", { name: "Users" }));

  expect(screen.getByTestId("at")).toHaveTextContent("/admin/users");
  expect(await screen.findByRole("heading", { name: "Users" })).toBeInTheDocument();
});
