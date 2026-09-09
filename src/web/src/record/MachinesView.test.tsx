import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { MachinesView } from "./MachinesView";

function aMachine(key: string, status = "active", kind = "vps") {
  return {
    key,
    name: key,
    kind,
    status,
    provider: "example-hoster",
    location: "fsn1",
    arch: "amd64",
    measured_at: null,
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
