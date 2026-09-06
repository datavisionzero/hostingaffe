import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { Activate } from "./Activate";

afterEach(() => vi.unstubAllGlobals());

it("exchanges the bootstrap token when Continue is clicked", async () => {
  const instance = installInstance({
    "POST /session/bootstrap": { status: 204 },
    "GET /me": { id: "0199a000-0000-7000-8000-000000000001", kind: "user", name: "maintainer", administrator: true, email: "maintainer@example.test", owner: null, token: null, metadata: null, metadata_reported_at: null },
  });
  const activated = vi.fn();
  renderAt("/activate", <Activate onActivated={activated} />);
  const user = userEvent.setup();

  await user.type(screen.getByLabelText("Bootstrap token"), "a-bootstrap-token-that-is-long-enough");
  await user.type(screen.getByLabelText("Password"), "a secure password");
  await user.click(screen.getByRole("button", { name: "Continue" }));

  expect(instance.calls.map((request) => `${request.method} ${new URL(request.url).pathname}`)).toEqual(["POST /session/bootstrap", "GET /me"]);
  expect(activated).toHaveBeenCalledOnce();
});
