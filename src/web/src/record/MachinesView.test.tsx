import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { MachinesView } from "./MachinesView";

function aMachine(
  key: string,
  status = "active",
  kind = "vps",
  lastSeen: string | null = null,
  rebootRequired: boolean | null = null,
) {
  return {
    key,
    name: key,
    kind,
    status,
    provider: "example-hoster",
    location: "fsn1",
    arch: "amd64",
    measured_at: null,
    last_seen: lastSeen,
    reboot_required: rebootRequired,
    updated_at: "2026-09-02T10:00:00Z",
  };
}

function list(at = "/machines") {
  const instance = installInstance({
    "GET /api/machines": (request) => {
      const query = new URL(request.url).searchParams;
      return query.get("kind") === "dedicated" ? [] : [aMachine("web-01")];
    },
  });

  renderAt(at, <MachinesView />);

  return instance;
}

afterEach(() => vi.unstubAllGlobals());

describe("the machines (VISION 6.2)", () => {
  it("lists them with what a row is worth reading for", async () => {
    list();

    const row = await screen.findByRole("link", { name: /web-01/ });
    expect(row).toHaveAttribute("href", "/machines/web-01");
    expect(screen.getByText("example-hoster")).toBeInTheDocument();
    // Never measured is a fact about the row, not an empty cell.
    expect(screen.getByText("never measured")).toBeInTheDocument();
  });

  it("draws each machine's avatar before its key", async () => {
    installInstance({ "GET /api/machines": [{ ...aMachine("web-01"), avatar: "owl", avatar_color: "red" }] });
    renderAt("/machines", <MachinesView />);

    expect(await screen.findByRole("img", { name: "red owl" })).toBeInTheDocument();
  });

  // The list somebody works off on a Friday afternoon: which of them are
  // waiting for a restart. A word, no colour and no threshold — and nothing at
  // all where the machine never said (VISION 5).
  it("says which machines are waiting for a restart, and nothing where it does not know", async () => {
    installInstance({
      "GET /api/machines": [
        aMachine("web-01", "active", "vps", "2026-09-13T08:00:00Z", true),
        aMachine("web-02", "active", "vps", "2026-09-13T08:00:00Z", false),
        aMachine("web-03"),
      ],
    });

    renderAt("/machines", <MachinesView />);

    await screen.findByRole("link", { name: /web-01/ });
    expect(screen.getAllByText("restart")).toHaveLength(1);
  });

  // The chips are the closed sets; the name of a machine is not one of them,
  // and half a name is what somebody actually remembers. It narrows what the
  // instance already answered, because a team's machines are a screenful.
  it("narrows by a typed word, over the key and the name alike", async () => {
    const instance = installInstance({
      "GET /api/machines": [aMachine("web-01"), { ...aMachine("db-01"), name: "The database" }],
    });

    renderAt("/machines", <MachinesView />);

    await screen.findByRole("link", { name: /web-01/ });
    const before = instance.calls.length;

    await userEvent.type(screen.getByRole("searchbox", { name: "Find" }), "database");

    await waitFor(() => expect(screen.queryByRole("link", { name: /web-01/ })).not.toBeInTheDocument());
    expect(screen.getByRole("link", { name: /db-01/ })).toBeInTheDocument();
    expect(instance.calls).toHaveLength(before);
  });

  // A filtered list is something people send each other, so it is in the
  // address and not in a component's memory.
  it("takes its narrowing from the address and puts it back there", async () => {
    const instance = list("/machines?status=retired");

    await waitFor(() =>
      expect(instance.calls.some((call) => new URL(call.url).searchParams.get("status") === "retired")).toBe(true));

    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "dedicated" }));

    await waitFor(() =>
      expect(instance.calls.some((call) => new URL(call.url).searchParams.get("kind") === "dedicated")).toBe(true));
  });

  it("asks for retired machines only when they are asked for", async () => {
    const instance = list();

    await screen.findByRole("link", { name: /web-01/ });
    expect(instance.calls.every((call) => new URL(call.url).searchParams.get("retired") === null)).toBe(true);

    const user = userEvent.setup();
    await user.click(screen.getByRole("checkbox", { name: "Include retired" }));

    await waitFor(() =>
      expect(instance.calls.some((call) => new URL(call.url).searchParams.get("retired") === "true")).toBe(true));
  });

  // An empty record and an empty filtered result are different states: one is a
  // record nobody has written in yet, the other is a filter that matched nothing.
  it("tells an empty record apart from a filter that matched nothing", async () => {
    list("/machines?kind=dedicated");

    expect(await screen.findByText("Nothing matches.")).toBeInTheDocument();
    expect(screen.queryByText(/rents or owns/)).not.toBeInTheDocument();
  });
});

describe("when each machine last spoke (VISION 7, ADR 0015)", () => {
  it("says how long ago, and nothing where nothing ever came", async () => {
    installInstance({
      "GET /api/machines": [
        aMachine("ex44", "active", "dedicated", new Date(Date.now() - 12 * 60 * 1000).toISOString()),
        aMachine("cx22"),
      ],
    });

    renderAt("/machines", <MachinesView />);

    expect(await screen.findByRole("link", { name: /ex44/ })).toBeInTheDocument();
    // Relative, because the question is "how long has it been quiet" and not
    // "what was the timestamp".
    expect(screen.getByText(/minutes ago/)).toBeInTheDocument();

    // No threshold, no colour, no badge: a machine that never reported says
    // nothing rather than being called anything.
    for (const judgement of ["stale", "silent", "down", "offline"]) {
      expect(screen.queryByText(new RegExp(judgement, "i"))).not.toBeInTheDocument();
    }
  });

  it("puts the quietest first when asked, and never before a month ago", async () => {
    installInstance({
      "GET /api/machines": [
        aMachine("a-loud", "active", "vps", new Date(Date.now() - 60 * 1000).toISOString()),
        aMachine("b-quiet", "active", "vps", new Date(Date.now() - 30 * 86400 * 1000).toISOString()),
        aMachine("c-never"),
      ],
    });

    renderAt("/machines?quietest=yes", <MachinesView />);

    await screen.findByRole("link", { name: /a-loud/ });
    const rows = screen.getAllByRole("link").map((row) => row.getAttribute("href"));
    expect(rows).toEqual(["/machines/c-never", "/machines/b-quiet", "/machines/a-loud"]);
  });
});
