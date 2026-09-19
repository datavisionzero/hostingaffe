import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { HistoryView } from "./HistoryView";

const actor = { id: "0199a000-0000-7000-8000-000000000001", kind: "user", name: "maintainer" };

function anEvent(over: Record<string, unknown> = {}) {
  return {
    at: "2026-09-18T19:12:04.118231Z",
    actor,
    subject_kind: "installation",
    subject: "logaffe-prod",
    number: null,
    machine: "ex44",
    owner: null,
    changes: [{ field: "status", old_value: "planned", new_value: "active" }],
    note: null,
    cursor: "one",
    ...over,
  };
}

function feed(events: unknown[], at = "/history") {
  const instance = installInstance({
    "GET /api/history": (request) => {
      const query = new URL(request.url).searchParams;
      return query.get("before") === null
        ? events
        : [anEvent({ at: "2026-09-01T08:00:00Z", subject_kind: "machine", subject: "ex44", cursor: "later" })];
    },
    "GET /api/machines": [{ key: "ex44", name: "ex44", kind: "vps", status: "active", updated_at: "2026-09-02T10:00:00Z" }],
  });

  renderAt(at, <HistoryView />);

  return instance;
}

afterEach(() => vi.unstubAllGlobals());

describe("what has been going on (VISION 6.2)", () => {
  it("is one line per act, with the fields that act changed", async () => {
    feed([
      anEvent({
        changes: [
          { field: "status", old_value: "planned", new_value: "active" },
          { field: "backup", old_value: "planned", new_value: "active" },
        ],
        note: "the box is live",
      }),
    ]);

    const line = await screen.findByRole("listitem");

    expect(within(line).getByRole("link", { name: "logaffe-prod" }))
      .toHaveAttribute("href", "/installations/logaffe-prod");
    expect(line).toHaveTextContent("status");
    expect(line).toHaveTextContent("backup");
    expect(line).toHaveTextContent("the box is live");
    // The day it happened on is a heading over it, not a column in it.
    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent("2026");
  });

  /** The event this reading exists for: a version that moved. */
  it("reads a deployment as the version it went to", async () => {
    feed([
      anEvent({
        subject_kind: "deployment",
        number: 7,
        changes: [{ field: "version", old_value: "0.4.1", new_value: "0.5.0" }],
        cursor: "deployed",
      }),
    ]);

    const line = await screen.findByRole("listitem");

    expect(line).toHaveTextContent("#7");
    expect(line).toHaveTextContent("0.4.1");
    expect(line).toHaveTextContent("0.5.0");
  });

  it("links a file under the thing it belongs to, and links nothing the purge has taken", async () => {
    feed([
      anEvent({
        subject_kind: "file",
        subject: "compose.override.yml",
        owner: { kind: "installation", key: "logaffe-prod" },
        changes: [{ field: "content", old_value: null, new_value: null }],
        cursor: "file",
      }),
      anEvent({ subject_kind: "machine", subject: null, machine: null, cursor: "gone" }),
    ]);

    expect(await screen.findByRole("link", { name: "compose.override.yml" }))
      .toHaveAttribute("href", "/installations/logaffe-prod/files/compose.override.yml");
    expect(screen.getByText("gone")).toBeInTheDocument();
  });

  /**
   * The birth of a file: the subject is the path, and a `created` carrying that
   * same path as its new value says the word twice over.
   */
  it("does not print a birth's subject back at it", async () => {
    feed([
      anEvent({
        subject_kind: "file",
        subject: "compose.override.yml",
        owner: { kind: "installation", key: "logaffe-prod" },
        changes: [{ field: "created", old_value: null, new_value: "compose.override.yml" }],
        cursor: "born",
      }),
    ]);

    const line = await screen.findByRole("listitem");

    expect(line).toHaveTextContent("created");
    expect(line.textContent).not.toContain("→");
    // Once, as the subject; the change adds nothing to it.
    expect(line.textContent?.match(/compose\.override\.yml/g)).toHaveLength(1);
  });

  it("writes a moment the way every other date on the screen is written", async () => {
    feed([
      anEvent({
        subject_kind: "machine",
        subject: "ex44",
        changes: [
          { field: "os", old_value: "Debian 12", new_value: "Debian 13" },
          { field: "measured_at", old_value: null, new_value: "2026-09-19T08:00:00.000000Z" },
        ],
        cursor: "measured",
      }),
    ]);

    const line = await screen.findByRole("listitem");

    expect(line).toHaveTextContent(new Date("2026-09-19T08:00:00.000000Z").toLocaleString());
    expect(line.textContent).not.toContain("2026-09-19T08:00:00.000000Z");
    // And a value that is no moment is untouched beside it.
    expect(line).toHaveTextContent("Debian 13");
  });

  it("walks on from the cursor of the last event it shows", async () => {
    const instance = feed(Array.from({ length: 50 }, (_, index) => anEvent({ cursor: `c${String(index)}` })));

    await userEvent.click(await screen.findByRole("button", { name: "Load more" }));

    const asked = instance.calls.map((call) => new URL(call.url));
    expect(asked.some((url) => url.searchParams.get("before") === "c49")).toBe(true);
    expect(await screen.findByRole("link", { name: "ex44" })).toBeInTheDocument();

    // The second page came back short, so the walk says it is over.
    expect(await screen.findByText("That is everything.")).toBeInTheDocument();
  });

  it("narrows to one machine and says so, with the way back", async () => {
    const instance = feed([anEvent()], "/history?machine=ex44");

    expect(await screen.findByRole("link", { name: "ex44" })).toHaveAttribute("href", "/machines/ex44");

    const asked = instance.calls.map((call) => new URL(call.url)).filter((url) => url.pathname === "/api/history");
    expect(asked[0]?.searchParams.get("machine")).toBe("ex44");
  });
});
