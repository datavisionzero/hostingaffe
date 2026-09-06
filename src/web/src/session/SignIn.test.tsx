import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { SignIn } from "./SignIn";

afterEach(() => vi.unstubAllGlobals());

describe("password sign in", () => {
  it("creates a session and asks who it belongs to", async () => {
    const { calls } = installInstance({ "POST /api/session": { status: 204 }, "GET /api/me": aUser });
    const signedIn = vi.fn(); renderAt("/login", <SignIn onSignedIn={signedIn} />); const user = userEvent.setup();
    await user.type(screen.getByLabelText("Email"), "maintainer@example.test");
    await user.type(screen.getByLabelText("Password"), "a long password");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await vi.waitFor(() => signedIn.mock.calls[0]?.[0])).toEqual(aUser);
    expect(await calls[0].json()).toEqual({ email: "maintainer@example.test", password: "a long password" });
    expect(calls[0].headers.get("X-Hostingaffe-CSRF")).toBe("1");
  });

  // Recover and Activate open with an `<h1>`; sign-in was the odd one out.
  it("opens with a heading, like the other screens in the same frame", () => {
    installInstance({});
    renderAt("/login", <SignIn onSignedIn={vi.fn()} />);

    expect(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
  });

  it("shows the indistinguishable refusal", async () => {
    installInstance({ "POST /api/session": { status: 401, body: { detail: "The email or password is not correct." } } });
    renderAt("/login", <SignIn onSignedIn={vi.fn()} />); const user = userEvent.setup();
    await user.type(screen.getByLabelText("Email"), "nobody@example.test"); await user.type(screen.getByLabelText("Password"), "wrong");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("email or password");
  });
});
