import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useSearchParams } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { InstallationsView } from "./InstallationsView";

function anInstallation(key: string, name = key, machine = "web-01", environment = "production", status = "active") {
  return {
    key,
    name,
    machine,
    software: "caddy",
    environment,
    role: "platform",
    status,
    backup: "none",
    monitoring: "none",
    logging: "none",
    version: "2.8.4",
    updated_at: "2026-09-02T10:00:00Z",
  };
}

function aMachine(key: string, name: string) {
  return {
    key,
    name,
    kind: "vps",
    status: "active",
    provider: "example-hoster",
    location: "fsn1",
    arch: "amd64",
    measured_at: null,
    last_seen: null,
    reboot_required: null,
    updated_at: "2026-09-02T10:00:00Z",
  };
}

/** The instance every test here asks, answering the query the way the endpoint would. */
function list(at: string, installations = [anInstallation("web-01-caddy", "Caddy")]) {
  const instance = installInstance({
    "GET /api/installations": (request) => {
      const query = new URL(request.url).searchParams;
      const machine = query.get("machine");

      return installations.filter((installation) => machine === null || installation.machine === machine);
    },
    "GET /api/machines": [aMachine("web-01", "The web server"), aMachine("db-01", "The database")],
  });

  renderAt(at, <><InstallationsView /><Address /></>);

  return instance;
}

/**
 * The address the screen is on. The narrowing belongs in it and not in the
 * component, so the tests read it where a reader would copy it from — under a
 * memory router, which is where `window.location` is not.
 */
function Address() {
  const [params] = useSearchParams();

  return <output data-testid="address">{params.toString()}</output>;
}

function address(): string {
  return screen.getByTestId("address").textContent ?? "";
}

function asked(instance: { calls: Request[] }, path: string): URLSearchParams[] {
  return instance.calls
    .map((call) => new URL(call.url))
    .filter((url) => url.pathname === path)
    .map((url) => url.searchParams);
}

afterEach(() => vi.unstubAllGlobals());

describe("the installations (VISION 7)", () => {
  // The question people come to this screen with is about what is running now,
  // in production — so that is what an address naming nothing narrows to.
  it("starts on what is active in production, and says so where it can be taken off", async () => {
    const instance = list("/installations");

    await waitFor(() =>
      expect(asked(instance, "/api/installations").some((query) =>
        query.get("environment") === "production" && query.get("status") === "active")).toBe(true));

    // Visible, and one click from gone: a narrowing nobody can see reads as a
    // record that lost something.
    expect(screen.getByRole("button", { name: "production", pressed: true })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "active", pressed: true })).toBeInTheDocument();
  });

  // A preset that only the component knows would make a pasted link show
  // something else than the screen it was copied from.
  it("writes the preset into the address, and takes it off there as well", async () => {
    list("/installations");

    await screen.findByRole("button", { name: "production", pressed: true });
    expect(address()).toContain("environment=production");

    await userEvent.click(screen.getByRole("button", { name: "production" }));

    // Emptied rather than deleted: an address that named nothing would get the
    // preset back, and there would be no way to ask for every environment.
    await waitFor(() => expect(address()).toMatch(/(^|&)environment=(&|$)/));
    expect(screen.getByRole("button", { name: "production", pressed: false })).toBeInTheDocument();
  });

  // A link from the machine screen means every installation there, not every
  // active production one.
  it("leaves an address that already narrows alone", async () => {
    const instance = list("/installations?machine=db-01", [
      anInstallation("web-01-caddy", "Caddy"),
      anInstallation("db-01-postgres", "Postgres", "db-01", "staging"),
    ]);

    await screen.findByRole("link", { name: /db-01-postgres/ });
    expect(asked(instance, "/api/installations").every((query) => query.get("status") === null)).toBe(true);
  });

  it("narrows to one machine from the list itself", async () => {
    const instance = list("/installations", [
      anInstallation("web-01-caddy", "Caddy"),
      anInstallation("db-01-postgres", "Postgres", "db-01"),
    ]);

    await screen.findByRole("link", { name: /web-01-caddy/ });

    await userEvent.click(screen.getByRole("combobox", { name: "Machine" }));
    await userEvent.click(await screen.findByRole("option", { name: /db-01/ }));

    await waitFor(() =>
      expect(asked(instance, "/api/installations").some((query) => query.get("machine") === "db-01")).toBe(true));
    expect(address()).toContain("machine=db-01");
  });

  // The word narrows what the endpoint already answered: key and name, a part
  // of either, because that is what somebody remembers half of.
  it("narrows by a typed word, without asking the instance again", async () => {
    const instance = list("/installations", [
      anInstallation("web-01-caddy", "Caddy"),
      anInstallation("web-01-postgres", "Postgres"),
    ]);

    await screen.findByRole("link", { name: /web-01-caddy/ });
    const before = asked(instance, "/api/installations").length;

    await userEvent.type(screen.getByRole("searchbox", { name: "Find" }), "postgres");

    await waitFor(() => expect(screen.queryByRole("link", { name: /web-01-caddy/ })).not.toBeInTheDocument());
    expect(screen.getByRole("link", { name: /web-01-postgres/ })).toBeInTheDocument();
    expect(asked(instance, "/api/installations")).toHaveLength(before);
  });

  // An empty record and an empty result are different states, and the preset
  // makes the second one the usual one: a first installation that is still
  // planned must not read as a record with nothing in it.
  it("says which empty it is", async () => {
    list("/installations", []);

    expect(await screen.findByText("Nothing matches.")).toBeInTheDocument();
  });
});
