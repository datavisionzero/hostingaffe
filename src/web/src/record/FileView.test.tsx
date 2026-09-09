import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { FileView } from "./FileView";

const identity = { id: aUser.id, kind: "user", name: aUser.name };

function aFile(revision: number, content: string) {
  return {
    owner: { kind: "installation", key: "logaffe-prod" },
    path: "compose.override.yml",
    executable: false,
    content,
    revision,
    created_by: identity,
    updated_by: identity,
    created_at: "2026-09-02T10:00:00Z",
    updated_at: "2026-09-04T10:00:00Z",
  };
}

const head = aFile(3, "services:\n  logaffe:\n    image: logaffe:2\n");
const older = aFile(2, "services:\n  logaffe:\n    image: logaffe:1\n");

function view(at: string, routes: Parameters<typeof installInstance>[0] = {}) {
  const instance = installInstance({
    "GET /api/installations/logaffe-prod/files/compose.override.yml": (request) =>
      new URL(request.url).searchParams.get("revision") === "2" ? older : head,
    "GET /api/installations/logaffe-prod/file-revisions/compose.override.yml": [
      { revision: 3, executable: false, by: identity, at: "2026-09-04T10:00:00Z" },
      { revision: 2, executable: false, by: identity, at: "2026-09-03T10:00:00Z" },
    ],
    ...routes,
  });

  renderAt(
    at,
    <Routes>
      <Route path="/installations/:key/files/*" element={<FileView owner="installation" />} />
    </Routes>,
  );

  return instance;
}

afterEach(() => vi.unstubAllGlobals());

describe("a file of the record (VISION 6.2)", () => {
  it("shows the current content and every revision beside it", async () => {
    view("/installations/logaffe-prod/files/compose.override.yml");

    expect(await screen.findByText(/image: logaffe:2/)).toBeInTheDocument();
    expect(screen.getByText("rev 2")).toBeInTheDocument();
  });

  // The path carries slashes and is the rest of the address, not one segment.
  it("reads a file whose path has directories in it", async () => {
    const instance = view("/installations/logaffe-prod/files/etc/caddy/Caddyfile", {
      "GET /api/installations/logaffe-prod/files/etc/caddy/Caddyfile": { ...head, path: "etc/caddy/Caddyfile" },
      "GET /api/installations/logaffe-prod/file-revisions/etc/caddy/Caddyfile": [],
    });

    expect(await screen.findByText("etc/caddy/Caddyfile")).toBeInTheDocument();
    expect(instance.calls.some((call) => new URL(call.url).pathname.endsWith("/files/etc/caddy/Caddyfile"))).toBe(true);
  });

  it("reads an older revision when the address asks for one, and says it is old", async () => {
    view("/installations/logaffe-prod/files/compose.override.yml?revision=2");

    expect(await screen.findByText(/image: logaffe:1/)).toBeInTheDocument();
    expect(screen.getByText(/as it was/)).toBeInTheDocument();
    // An old revision is not written over: the way to bring it back is a new write.
    expect(screen.queryByRole("button", { name: "Edit" })).not.toBeInTheDocument();
  });

  it("shows what changed between two revisions", async () => {
    view("/installations/logaffe-prod/files/compose.override.yml?against=2");

    expect(await screen.findByText(/Difference against revision 2/)).toBeInTheDocument();
    // The line that went and the line that came, and the two that stayed.
    expect(screen.getByText("image: logaffe:1")).toBeInTheDocument();
    expect(screen.getByText("image: logaffe:2")).toBeInTheDocument();
    expect(screen.getByText("services:")).toBeInTheDocument();
  });

  // A file is guarded with its revision and not with a timestamp
  // (`docs/api.md`, Guarding a write).
  it("writes a new revision guarded by the one it read", async () => {
    const instance = view("/installations/logaffe-prod/files/compose.override.yml", {
      "PUT /api/installations/logaffe-prod/files/compose.override.yml": aFile(4, "changed"),
    });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Edit" }));
    await user.click(await screen.findByRole("button", { name: "Write a new revision" }));

    await waitFor(() => {
      const write = instance.calls.find((call) => call.method === "PUT");
      expect(write?.headers.get("If-Match")).toBe("3");
    });
  });

  it("keeps the typed text and says what to do when somebody else wrote first", async () => {
    view("/installations/logaffe-prod/files/compose.override.yml", {
      "PUT /api/installations/logaffe-prod/files/compose.override.yml": {
        status: 412,
        body: { type: "/problems/stale", title: "stale", status: 412, current: aFile(4, "somebody else's text") },
      },
    });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Edit" }));
    const field = screen.getByRole("textbox", { name: "File content" });
    await user.clear(field);
    await user.type(field, "mine");
    await user.click(screen.getByRole("button", { name: "Write a new revision" }));

    expect(await screen.findByText(/it is at revision 4 now/)).toBeInTheDocument();
    // The typed text is still there to be saved again, over what came back.
    expect(field).toHaveValue("mine");
    expect(screen.getByText("somebody else's text")).toBeInTheDocument();
  });
});
