import { screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { OverviewView } from "./OverviewView";

function aMachine(over: Record<string, unknown> = {}) {
  return {
    key: "ex44",
    name: "ex44",
    kind: "dedicated",
    status: "active",
    provider: "example-hoster",
    location: "fsn1",
    arch: "arm64",
    measured_at: null,
    last_seen: "2026-09-19T11:00:00Z",
    reboot_required: false,
    updated_at: "2026-09-02T10:00:00Z",
    activity: {
      window: "7d",
      changes: 3,
      deployments: 1,
      latest: {
        installation: "logaffe-prod",
        number: 7,
        version: "0.5.0",
        previous: "0.4.1",
        at: "2026-09-19T05:00:00Z",
      },
      installations: 7,
      drift: 2,
    },
    ...over,
  };
}

function overview(machines: unknown[], at = "/") {
  const instance = installInstance({ "GET /api/machines": machines });

  renderAt(at, <OverviewView />);

  return instance;
}

afterEach(() => vi.unstubAllGlobals());

describe("the overview (ADR 0018)", () => {
  it("is a tile per machine, and the tile leads to what happened on it", async () => {
    overview([aMachine()]);

    const tile = await screen.findByRole("listitem");

    expect(within(tile).getByRole("link", { name: /What has been going on on ex44/ }))
      .toHaveAttribute("href", "/history?machine=ex44");
    // And the key itself leads to the machine.
    expect(within(tile).getByRole("link", { name: "ex44" })).toHaveAttribute("href", "/machines/ex44");
  });

  it("says what lately happened: the version that moved, the count, the report, the drift", async () => {
    overview([aMachine()]);

    const tile = await screen.findByRole("listitem");

    expect(within(tile).getByRole("link", { name: "logaffe-prod" }))
      .toHaveAttribute("href", "/installations/logaffe-prod");
    expect(tile).toHaveTextContent("0.4.1");
    expect(tile).toHaveTextContent("0.5.0");
    // A deployment is a change like any other in the count.
    expect(tile).toHaveTextContent("4 changes in 7d");
    expect(tile).toHaveTextContent("7 installations");
    expect(tile).toHaveTextContent("2 drift");
  });

  /** The two ordinary states of a host nobody has set a cron up on. */
  it("says that a machine never reported and that nothing was ever deployed", async () => {
    overview([
      aMachine({
        last_seen: null,
        activity: { window: "7d", changes: 0, deployments: 0, latest: null, installations: 0, drift: 0 },
      }),
    ]);

    const tile = await screen.findByRole("listitem");

    expect(tile).toHaveTextContent("Never reported");
    expect(tile).toHaveTextContent("No deployment recorded.");
    expect(tile).toHaveTextContent("Nothing in 7d");
    // No drift is no line, not a zero.
    expect(tile).not.toHaveTextContent("0 drift");
  });

  it("says that a machine is waiting for a restart, and nothing where it does not know", async () => {
    overview([aMachine({ key: "ex52", reboot_required: true }), aMachine({ reboot_required: null })]);

    const tiles = await screen.findAllByRole("listitem");

    expect(tiles[0]).toHaveTextContent("Restart pending");
    expect(tiles[1]).not.toHaveTextContent("Restart pending");
  });

  it("asks for the window it shows, and for the one the address names", async () => {
    const instance = overview([aMachine()], "/?window=24h");

    await screen.findByRole("listitem");

    const asked = new URL(instance.calls[0]!.url).searchParams;
    expect(asked.get("activity")).toBe("24h");
  });
});
