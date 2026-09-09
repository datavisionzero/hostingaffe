import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { Device } from "./Device";

afterEach(() => vi.unstubAllGlobals());

it("reads back the login a code in the address names, and approves it", async () => {
  const instance = installInstance({
    "GET /api/device/logins/BCDF-GHJK": {
      user_code: "BCDF-GHJK",
      requested_at: "2026-09-09T10:00:00Z",
      expires_at: "2026-09-09T10:10:00Z",
      state: "pending",
    },
    "POST /api/device/approvals": { user_code: "BCDF-GHJK", requested_at: "2026-09-09T10:00:00Z", expires_at: "2026-09-09T10:10:00Z", state: "approved" },
  });
  renderAt("/device?code=BCDF-GHJK", <Device name="maintainer" />);
  const user = userEvent.setup();

  expect(await screen.findByDisplayValue("BCDF-GHJK")).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Approve" }));

  expect(await screen.findByText("That machine is signed in")).toBeInTheDocument();
  expect(instance.calls.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
    "GET /api/device/logins/BCDF-GHJK",
    "POST /api/device/approvals",
  ]);
});

it("refuses a login nobody at this terminal started, and hands nothing over", async () => {
  const instance = installInstance({
    "POST /api/device/refusals": { user_code: "BCDF-GHJK", requested_at: "2026-09-09T10:00:00Z", expires_at: "2026-09-09T10:10:00Z", state: "denied" },
  });
  renderAt("/device", <Device name="maintainer" />);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Code"), "BCDF-GHJK");
  await user.click(screen.getByRole("button", { name: "I did not start this" }));

  expect(await screen.findByText("That login was refused")).toBeInTheDocument();
  expect(instance.calls.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
    "POST /api/device/refusals",
  ]);
});

it("says so when the code names no waiting login", async () => {
  installInstance({
    "POST /api/device/approvals": {
      status: 404,
      body: { type: "/problems/not-found", title: "Nothing by that key or id", status: 404, detail: "No login is waiting for that code." },
    },
  });
  renderAt("/device", <Device name="maintainer" />);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Code"), "BCDF-GHJK");
  await user.click(screen.getByRole("button", { name: "Approve" }));

  expect(await screen.findByRole("alert")).toHaveTextContent("No login is waiting for that code.");
});
