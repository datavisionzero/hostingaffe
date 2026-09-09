import { screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";
import { aUser, installInstance, renderAt } from "@/shared/testing";
import { InstallationView } from "./InstallationView";

const identity = { id: aUser.id, kind: "user", name: aUser.name };

const installation = {
  key: "logaffe-prod",
  name: "logaffe",
  machine: "web-01",
  software: "logaffe",
  environment: "production",
  role: "application",
  status: "active",
  urls: ["https://logs.example.test"],
  ports: [{ port: 18502, protocol: "tcp", scope: "private" }],
  path: "/srv/logaffe",
  secrets: ["logaffe/postgres-password"],
  backup: "active",
  monitoring: "external",
  logging: "central",
  version: "2.1.0",
  description: "The log sink everything else writes into.",
  created_by: identity,
  updated_by: identity,
  created_at: "2026-09-02T10:00:00Z",
  updated_at: "2026-09-04T10:00:00Z",
};

function deployment(number: number, version: string, previous: string | null) {
  return {
    number,
    version,
    previous,
    ref: null,
    at: `2026-09-0${number}T10:00:00Z`,
    by: identity,
    ticket: null,
    updated_at: "2026-09-04T10:00:00Z",
  };
}

function view(routes: Parameters<typeof installInstance>[0] = {}) {
  const instance = installInstance({
    "GET /api/installations/logaffe-prod": installation,
    "GET /api/installations/logaffe-prod/deployments": [
      deployment(1, "2.0.0", null),
      deployment(2, "2.1.0", "2.0.0"),
    ],
    "GET /api/installations/logaffe-prod/history": [],
    "GET /api/installations/logaffe-prod/files": [],
    "GET /api/pages": [],
    ...routes,
  });

  renderAt(
    "/installations/logaffe-prod",
    <Routes><Route path="/installations/:key" element={<InstallationView />} /></Routes>,
  );

  return instance;
}

afterEach(() => vi.unstubAllGlobals());

describe("an installation (VISION 6.2)", () => {
  it("names both ends of it, and links to each", async () => {
    view();

    expect(await screen.findByRole("link", { name: "web-01" })).toHaveAttribute("href", "/machines/web-01");
    expect(screen.getByRole("link", { name: "logaffe" })).toHaveAttribute("href", "/software/logaffe");
  });

  it("shows the ports and the names of the secrets", async () => {
    view();

    expect(await screen.findByText("18502/tcp private")).toBeInTheDocument();
    // The name of the secret, never the secret: this instance records where one
    // lives, and vaultaffe holds it (VISION 11).
    expect(screen.getByText("logaffe/postgres-password")).toBeInTheDocument();
  });

  // The version on an installation is the newest deployment's, so the newest
  // one is the row a reader's eye should land on first.
  it("puts the newest deployment at the top", async () => {
    view();

    const rows = await screen.findAllByText(/^#\d$/);
    expect(rows.map((row) => row.textContent)).toEqual(["#2", "#1"]);
    expect(within(rows[0].parentElement!).getByText("from 2.0.0")).toBeInTheDocument();
  });

  it("says so when nothing was ever deployed here", async () => {
    view({ "GET /api/installations/logaffe-prod/deployments": [] });

    expect(await screen.findByText(/Nothing has been deployed here yet/)).toBeInTheDocument();
  });
});
